using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Runner;

internal sealed class FixedClock(DateTime instant) : ISystemClock { public DateTime UtcNow => instant; }

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = (args.Length > 0 ? args[0] : "help").ToLowerInvariant();
        if (command is "help" or "--help" or "-h")
        {
            PrintHelp(); return 0;
        }
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__BetaDb");
        if (string.IsNullOrWhiteSpace(conn))
        {
            Console.Error.WriteLine("Missing ConnectionStrings__BetaDb environment variable.");
            return 2;
        }
        var options = new DbContextOptionsBuilder<BetaDbContext>()
            .UseSqlServer(conn, sql => sql.MigrationsAssembly(typeof(BetaDbContext).Assembly.FullName))
            .Options;
        try
        {
            await using var db = new BetaDbContext(options);
            switch (command)
            {
                case "migrate":
                    await db.Database.MigrateAsync();
                    Console.WriteLine("EF Core migrations applied to the CONNECTED SQL Server database.");
                    return 0;
                case "status":
                    Console.WriteLine("Connected: " + await db.Database.CanConnectAsync());
                    Console.WriteLine("Applied migrations:");
                    foreach (var x in await db.Database.GetAppliedMigrationsAsync()) Console.WriteLine("  " + x);
                    Console.WriteLine("Pending migrations:");
                    foreach (var x in await db.Database.GetPendingMigrationsAsync()) Console.WriteLine("  " + x);
                    return 0;
                case "seed-demo":
                    await DemoSeeder.SeedAsync(db); return 0;
                case "seed-checkin-demo":
                    await SeedCheckInDemoAsync(options); return 0;
                case "smoke":
                    await SmokeAsync(options); return 0;
                case "race":
                    await RaceAsync(options); return 0;
                case "late":
                    await LatePaymentAsync(options); return 0;
                case "expire":
                    Console.WriteLine("Expired: " + await new OrderLifecycleService(db, new SystemClock()).ExpireDueAsync());
                    return 0;
                case "verify":
                    await IntegrityChecksAsync(db); return 0;
                default:
                    PrintHelp(); return 2;
            }
        }
        catch (BusinessRuleException ex)
        {
            Console.Error.WriteLine($"BUSINESS_RULE [{ex.Code}]: {ex.Message}"); return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}"); return 4;
        }
    }

    private static async Task SmokeAsync(DbContextOptions<BetaDbContext> options)
    {
        await using var db = new BetaDbContext(options);
        var customer = await db.Users.Where(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID")
            .Select(u => u.UserId).SingleAsync();
        var staff = await db.Users.Where(u => u.NormalizedEmail == "STAFF@DEMO.INVALID")
            .Select(u => u.UserId).SingleAsync();
        var showtime = await db.Showtimes.Where(s => s.Status == "ON_SALE" && s.StartUtc > DateTime.UtcNow)
            .OrderByDescending(s => s.ShowtimeId).FirstAsync();
        var available = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtime.ShowtimeId &&
            s.State == "AVAILABLE").Select(s => s.SeatId).Take(1).ToListAsync();
        if (available.Count == 0) throw new InvalidOperationException("No AVAILABLE seats left; seed another showtime.");
        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customer, showtime.ShowtimeId, available);
        var payments = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        var paymentId = await payments.CreateMockAttemptAsync(customer, reservation.OrderId, attemptKey);
        var message = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey,
            "MOCK-TXN-" + Guid.NewGuid().ToString("N"), reservation.OrderCode,
            reservation.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(message, MockPaymentSecurity.SignForDevelopment(message));
        var applied = await payments.ApplyVerifiedMockAsync(verified);
        if (applied.Decision != "APPLIED" || applied.OrderStatus != "PAID")
            throw new InvalidOperationException("Expected PAID + APPLIED.");
        var ticket = await db.Tickets.SingleAsync(t => t.OrderId == reservation.OrderId);
        var token = new QrTokenProtector().Unseal(ticket.QrTokenCipher);
        // Scan with a FIXED test time just before showtime, never alter persisted schedule.
        var scanClock = new FixedClock(DateTime.SpecifyKind(showtime.StartUtc.AddMinutes(-10), DateTimeKind.Utc));
        var scan = await new TicketScanningService(db, scanClock)
            .ScanAsync(staff, token, showtime.ShowtimeId);
        var duplicate = await new TicketScanningService(db, scanClock)
            .ScanAsync(staff, token, showtime.ShowtimeId);
        if (scan.Outcome != "ACCEPTED" || duplicate.Outcome != "ALREADY_USED")
            throw new InvalidOperationException("Expected ACCEPTED, then ALREADY_USED.");
        var again = await payments.ApplyVerifiedMockAsync(verified);
        if (again.Decision != "DUPLICATE") throw new InvalidOperationException("Payment was not idempotent.");
        Console.WriteLine($"SMOKE PASSED: order {reservation.OrderId}, payment {paymentId}, ticket {ticket.TicketId}");
        Console.WriteLine("Verified: PAID, QR accepted once, repeated scan blocked, callback idempotent.");
    }

    private static async Task RaceAsync(DbContextOptions<BetaDbContext> options)
    {
        await using var db = new BetaDbContext(options);
        var buyers = await db.Users.Where(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID" ||
            u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID")
            .OrderBy(u => u.UserId).Select(u => u.UserId).ToListAsync();
        if (buyers.Count != 2) throw new InvalidOperationException("Two demo customer users required.");
        var now = DateTime.UtcNow;
        var seat = await (from ss in db.ShowtimeSeats
            join showtime in db.Showtimes on ss.ShowtimeId equals showtime.ShowtimeId
            where showtime.Status == "ON_SALE" && showtime.StartUtc > now && ss.State == "AVAILABLE"
            select new { ss.ShowtimeId, ss.SeatId }).FirstAsync();
        var work = buyers.Select(async buyer =>
        {
            await using var local = new BetaDbContext(options);
            try
            {
                await new SeatReservationService(local, new SystemClock())
                    .ReserveAsync(buyer, seat.ShowtimeId, new[] { seat.SeatId });
                return "SUCCESS";
            }
            catch (BusinessRuleException ex) when (ex.Code is "SEAT_NOT_AVAILABLE" or "SEAT_CONFLICT")
            {
                return "CONFLICT";
            }
        });
        var results = await Task.WhenAll(work);
        if (results.Count(x => x == "SUCCESS") != 1 || results.Count(x => x == "CONFLICT") != 1)
            throw new InvalidOperationException("Concurrency invariant FAILED: " + string.Join(",", results));
        Console.WriteLine("RACE PASSED: one SUCCESS and one CONFLICT for a single showtime seat.");
    }

    private static async Task LatePaymentAsync(DbContextOptions<BetaDbContext> options)
    {
        await using var db = new BetaDbContext(options);
        var buyer = await db.Users.Where(u => u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID")
            .Select(u => u.UserId).SingleAsync();
        var showtime = await db.Showtimes.Where(s => s.Status == "ON_SALE" && s.StartUtc > DateTime.UtcNow)
            .OrderByDescending(s => s.ShowtimeId).FirstAsync();
        var available = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtime.ShowtimeId &&
            s.State == "AVAILABLE").Select(s => s.SeatId).Take(1).ToListAsync();
        if (available.Count == 0) throw new InvalidOperationException("Need an AVAILABLE seat for late-payment scenario.");
        var held = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(buyer, showtime.ShowtimeId, available);
        var attempt = Guid.NewGuid().ToString("N");
        var nowService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        await nowService.CreateMockAttemptAsync(buyer, held.OrderId, attempt);
        var lateClock = new FixedClock(DateTime.SpecifyKind(held.ExpiresUtc.AddSeconds(1), DateTimeKind.Utc));
        var expired = await new OrderLifecycleService(db, lateClock).ExpireAsync(held.OrderId);
        if (!expired) throw new InvalidOperationException("Expected order expiry.");
        var callback = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attempt,
            "MOCK-TXN-LATE-" + Guid.NewGuid().ToString("N"), held.OrderCode, held.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(callback, MockPaymentSecurity.SignForDevelopment(callback));
        var result = await new PaymentService(db, lateClock, new QrTokenProtector())
            .ApplyVerifiedMockAsync(verified);
        var payment = await db.Payments.SingleAsync(p => p.OrderId == held.OrderId);
        bool ticketIssued = await db.Tickets.AnyAsync(t => t.OrderId == held.OrderId);
        if (result.OrderStatus != "EXPIRED" || result.Decision != "NEEDS_REVIEW" ||
            payment.Status != "NEEDS_REVIEW" || ticketIssued)
            throw new InvalidOperationException("Late payment invariant FAILED.");
        Console.WriteLine("LATE PASSED: EXPIRED preserved, payment NEEDS_REVIEW, no ticket issued.");
    }

    private static async Task IntegrityChecksAsync(BetaDbContext db)
    {
        var checks = new (string Name, string Sql)[] {
            ("Wrong room/seat", "SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss JOIN dbo.Seats s ON s.seat_id=ss.seat_id JOIN dbo.Showtimes st ON st.showtime_id=ss.showtime_id WHERE s.room_id<>st.room_id OR ss.room_id<>st.room_id"),
            ("SOLD seat without PAID order", "SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss LEFT JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='SOLD' AND (o.order_id IS NULL OR o.status<>'PAID')"),
            ("PAID orders missing tickets", "SELECT COUNT_BIG(*) FROM dbo.Orders o OUTER APPLY (SELECT COUNT_BIG(*) n FROM dbo.Tickets t WHERE t.order_id=o.order_id) x WHERE o.status='PAID' AND x.n<>o.seat_count"),
            ("Order price snapshot mismatch", "SELECT COUNT_BIG(*) FROM dbo.Orders o OUTER APPLY (SELECT COALESCE(SUM(os.unit_price_vnd),0) v FROM dbo.OrderSeats os WHERE os.order_id=o.order_id) x WHERE x.v<>o.subtotal_vnd"),
            ("Late payment improperly SUCCESS", "SELECT COUNT_BIG(*) FROM dbo.Payments p JOIN dbo.Orders o ON o.order_id=p.order_id WHERE p.status='SUCCESS' AND o.status IN ('EXPIRED','CANCELLED')"),
            ("Dead order still holds seat", "SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='HELD' AND o.status IN ('CANCELLED','EXPIRED','FAILED','NEEDS_REVIEW')"),
            ("Cancelled showtime with pending order", "SELECT COUNT_BIG(*) FROM dbo.Orders o JOIN dbo.Showtimes s ON s.showtime_id=o.showtime_id WHERE s.status='CANCELLED' AND o.status='PENDING_PAYMENT'"),
            ("Archived movie with bookable showtime", "SELECT COUNT_BIG(*) FROM dbo.Showtimes s JOIN dbo.Movies m ON m.movie_id=s.movie_id WHERE m.status='ARCHIVED' AND s.status='ON_SALE' AND s.start_utc > SYSUTCDATETIME()"),
            ("Ticket issued on unpaid order or cancelled showtime", "SELECT COUNT_BIG(*) FROM dbo.Tickets t JOIN dbo.Orders o ON o.order_id=t.order_id JOIN dbo.Showtimes s ON s.showtime_id=t.showtime_id WHERE o.status<>'PAID' OR s.status='CANCELLED'")
        };
        long violations = 0;
        await db.Database.OpenConnectionAsync();
        foreach (var (name, sql) in checks)
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            var result = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            violations += result;
            Console.WriteLine($"{name}: {result} violation(s)");
        }
        if (violations != 0) throw new InvalidOperationException($"Integrity check found {violations} violation(s).");
        Console.WriteLine("INTEGRITY PASSED (static consistency checks; run race test separately).");
    }

    private static async Task SeedCheckInDemoAsync(DbContextOptions<BetaDbContext> options)
    {
        var optInCheckin = Environment.GetEnvironmentVariable("BGP_ALLOW_CHECKIN_DEMO");
        var optInDemo = Environment.GetEnvironmentVariable("BGP_ALLOW_DEMO_SEED");
        if (!string.Equals(optInCheckin, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(optInDemo, "true", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("CHECKIN DEMO BLOCKED: Set BGP_ALLOW_CHECKIN_DEMO=true to enable demo ticket generation.");
            Environment.ExitCode = 1;
            return;
        }

        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";
        if (string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("CHECKIN DEMO BLOCKED: Running under Production environment. Refusing execution.");
            Environment.ExitCode = 1;
            return;
        }

        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__BetaDb") ?? string.Empty;
        if (conn.Contains("production", StringComparison.OrdinalIgnoreCase) ||
            conn.Contains("prod.database", StringComparison.OrdinalIgnoreCase) ||
            conn.Contains("BetaDb_Prod", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("CHECKIN DEMO BLOCKED: Connection string appears to point to a PRODUCTION database. Refusing execution.");
            Environment.ExitCode = 1;
            return;
        }

        await using var db = new BetaDbContext(options);

        var customer = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID")
            ?? throw new InvalidOperationException("Demo user CUSTOMER1@DEMO.INVALID not found. Run 'seed-demo' first.");
        var manager = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            ?? throw new InvalidOperationException("Demo user MANAGER@DEMO.INVALID not found. Run 'seed-demo' first.");

        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Status == "PUBLISHED")
            ?? throw new InvalidOperationException("No PUBLISHED movies found in database. Run 'seed-demo' first.");

        var now = DateTime.UtcNow;

        // Look for an existing eligible showtime within check-in window (start_utc > now && start_utc <= now + 25m) with available seats
        var showtime = await db.Showtimes
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Where(s => s.Status == "ON_SALE" && s.StartUtc > now && s.StartUtc <= now.AddMinutes(25)
                && db.ShowtimeSeats.Any(ss => ss.ShowtimeId == s.ShowtimeId && ss.State == SeatStates.Available))
            .OrderByDescending(s => s.ShowtimeId)
            .FirstOrDefaultAsync();

        if (showtime == null)
        {
            // Publish a new showtime using ShowtimePublishingService to guarantee room schedule, buffer, and capacity rules
            var activeRooms = await db.Rooms.Where(r => r.Status == "ACTIVE").ToListAsync();
            Room? selectedRoom = null;
            DateTime targetStartUtc = DateTime.SpecifyKind(now.AddMinutes(15), DateTimeKind.Utc);
            var targetEndUtc = targetStartUtc.AddMinutes(movie.DurationMin);
            const short bufferMinutes = 15;

            foreach (var r in activeRooms)
            {
                var activeSeatsCount = await db.Seats.CountAsync(s => s.RoomId == r.RoomId && s.IsActive);
                if (activeSeatsCount == 0) continue;

                var hasOverlap = await db.Showtimes.AnyAsync(s => s.RoomId == r.RoomId && s.Status != "CANCELLED" &&
                    targetStartUtc < s.EndUtc.AddMinutes(s.BufferMinutes) &&
                    s.StartUtc < targetEndUtc.AddMinutes(bufferMinutes));

                if (!hasOverlap)
                {
                    selectedRoom = r;
                    break;
                }
            }

            if (selectedRoom == null)
            {
                throw new InvalidOperationException(
                    "Cannot publish demo showtime: all active rooms have overlapping schedules within the check-in window. Wait for existing showtimes to complete or resolve schedule conflicts.");
            }

            var activeRoomSeats = await db.Seats.Where(s => s.RoomId == selectedRoom.RoomId && s.IsActive).ToListAsync();
            var seatTypesInRoom = activeRoomSeats.Select(s => s.SeatTypeCode).Distinct().ToList();
            var priceInputs = seatTypesInRoom.Select(code => new SeatPriceInput(code, code == "VIP" ? 90000 : 75000)).ToList();

            var publishingService = new ShowtimePublishingService(db, new SystemClock());
            var newShowtimeId = await publishingService.PublishAsync(
                manager.UserId, selectedRoom.RoomId, movie.MovieId, targetStartUtc, bufferMinutes, priceInputs);

            showtime = await db.Showtimes
                .Include(s => s.Movie)
                .Include(s => s.Room)
                .SingleAsync(s => s.ShowtimeId == newShowtimeId);
        }

        var availableSeat = await db.ShowtimeSeats
            .Where(ss => ss.ShowtimeId == showtime.ShowtimeId && ss.State == SeatStates.Available)
            .Select(ss => ss.SeatId)
            .FirstOrDefaultAsync();

        if (availableSeat == 0)
        {
            throw new InvalidOperationException($"No AVAILABLE seats left in showtime #{showtime.ShowtimeId}.");
        }

        var res = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customer.UserId, showtime.ShowtimeId, [availableSeat]);

        var qrProtector = new QrTokenProtector();
        var payments = new PaymentService(db, new SystemClock(), qrProtector);
        var attemptKey = "DEMO-ATTEMPT-" + Guid.NewGuid().ToString("N");
        await payments.CreateMockAttemptAsync(customer.UserId, res.OrderId, attemptKey);

        var msg = new MockPaymentMessage(
            ProviderEventRef: "EVT-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-DEMO-" + Guid.NewGuid().ToString("N"),
            OrderCode: res.OrderCode,
            AmountVnd: res.TotalVnd,
            Outcome: "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        var applied = await payments.ApplyVerifiedMockAsync(verified);

        if (applied.Decision != "APPLIED" || applied.OrderStatus != "PAID")
        {
            throw new InvalidOperationException($"Payment application failed: {applied.Decision} / {applied.OrderStatus}");
        }

        var ticket = await db.Tickets
            .SingleAsync(t => t.OrderId == res.OrderId);
        var seatEntity = await db.Seats
            .SingleAsync(s => s.SeatId == ticket.SeatId);

        var rawToken = qrProtector.Unseal(ticket.QrTokenCipher);

        var showRawToken = string.Equals(Environment.GetEnvironmentVariable("BGP_SHOW_DEMO_QR_TOKEN"), "true", StringComparison.OrdinalIgnoreCase);
        var baseUrl = Environment.GetEnvironmentVariable("BGP_BASE_URL") ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';').FirstOrDefault() ?? "";
        if (!string.IsNullOrEmpty(baseUrl)) baseUrl = baseUrl.TrimEnd('/');
        var checkinPath = $"/staff/check-in?showtimeId={showtime.ShowtimeId}";
        var ticketPath = $"/orders/{res.OrderId}/tickets";

        Console.WriteLine();
        Console.WriteLine("======================================================================");
        Console.WriteLine("BETA GIAI PHONG — DEMO CHECK-IN TICKET GENERATED");
        Console.WriteLine("======================================================================");
        Console.WriteLine($"Showtime ID : {showtime.ShowtimeId}");
        Console.WriteLine($"Movie       : {showtime.Movie?.Title ?? movie.Title}");
        Console.WriteLine($"Room        : {showtime.Room?.RoomName}");
        Console.WriteLine($"Start Time  : {showtime.StartUtc:yyyy-MM-dd HH:mm:ss} UTC (Active check-in window)");
        Console.WriteLine($"Order Code  : {res.OrderCode}");
        Console.WriteLine($"Ticket Code : {ticket.TicketCode}");
        Console.WriteLine($"Seat        : {seatEntity.SeatCode} (Row {seatEntity.RowLabel}, Col {seatEntity.ColumnNo})");
        Console.WriteLine($"Customer    : {customer.DisplayName} ({customer.Email})");
        Console.WriteLine();
        Console.WriteLine($"Customer Ticket Route : {ticketPath}");
        Console.WriteLine($"Staff Check-In Route  : {(string.IsNullOrEmpty(baseUrl) ? checkinPath : $"{baseUrl}{checkinPath}")}");
        Console.WriteLine();
        if (showRawToken)
        {
            Console.WriteLine("[WARNING: BGP_SHOW_DEMO_QR_TOKEN=true] RAW QR TOKEN (Confidential — Development use only):");
            Console.WriteLine(rawToken);
        }
        else
        {
            Console.WriteLine("Raw QR token is masked by default for security.");
            Console.WriteLine("Set BGP_SHOW_DEMO_QR_TOKEN=true in your development shell to display raw token.");
        }
        Console.WriteLine("======================================================================");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Beta Giải Phóng EF Core data runner (SQL Server, prototype)");
        Console.WriteLine("Commands: migrate | status | seed-demo | seed-checkin-demo | smoke | race | late | expire | verify");
        Console.WriteLine("Environment: ConnectionStrings__BetaDb (required for commands)");
        Console.WriteLine("seed-demo: BGP_ALLOW_DEMO_SEED=true; BGP_DEMO_PASSWORD=... (>=12 chars)");
        Console.WriteLine("smoke: BGP_MOCK_HMAC_KEY_B64 and BGP_QR_AES_KEY_B64 (32 random bytes each)");
    }
}
