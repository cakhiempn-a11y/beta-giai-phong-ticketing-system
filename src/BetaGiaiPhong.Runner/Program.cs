using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Runner;

internal sealed class FixedClock(DateTime instant) : ISystemClock { public DateTime UtcNow => instant; }

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var command = (args.Length > 0 ? args[0] : "help").ToLowerInvariant();
        if (command == "create-e2e-db") return args.Length >= 3 ? await CreateE2eDatabaseAsync(args[1], args[2]) : 2;
        if (command == "drop-e2e-db") return args.Length >= 3 ? await DropE2eDatabaseAsync(args[1], args[2]) : 2;
        if (command == "e2e-sql") return args.Length >= 2 ? await ExecuteE2eSqlAsync(args[1]) : 2;
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
                case "seed-verified-showcase":
                    await DemoSeeder.SeedVerifiedShowcaseAsync(db); return 0;
                case "sync-tmdb-movie":
                    if (args.Length < 2 || !int.TryParse(args[1], out var tmdbId))
                    {
                        Console.Error.WriteLine("Usage: sync-tmdb-movie <tmdbId>");
                        return 2;
                    }
                    var tmdbSync = await new MovieContentSyncService(db, new SystemClock()).SyncMovieFromTmdbAsync(tmdbId);
                    Console.WriteLine($"TMDB_SYNC|{tmdbSync.Outcome}|MovieId={tmdbSync.MovieId?.ToString() ?? "NULL"}|{tmdbSync.Message}");
                    return tmdbSync.Outcome is "CREATED_DRAFT" or "UPDATED" or "MISSING_API_KEY" ? 0 : 1;
                case "sync-imdb-ratings":
                    if (args.Length < 2 || !File.Exists(args[1]))
                    {
                        Console.Error.WriteLine("Usage: sync-imdb-ratings <path-to-title.ratings.tsv-or-gz>");
                        return 2;
                    }
                    await using (var rawFile = File.OpenRead(args[1]))
                    await using (Stream ratingsStream = args[1].EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                        ? new System.IO.Compression.GZipStream(rawFile, System.IO.Compression.CompressionMode.Decompress)
                        : rawFile)
                    {
                        var imdbSync = await new MovieContentSyncService(db, new SystemClock()).ApplyImdbRatingsStreamAsync(ratingsStream);
                        Console.WriteLine($"IMDB_SYNC|Matched={imdbSync.MatchedMovies}|Updated={imdbSync.UpdatedMovies}|SkippedOverrides={imdbSync.SkippedManualOverrides}|RowsScanned={imdbSync.RowsScanned}");
                    }
                    return 0;
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
        Console.WriteLine("BETA GIAI PHONG â€” DEMO CHECK-IN TICKET GENERATED");
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
            Console.WriteLine("[WARNING: BGP_SHOW_DEMO_QR_TOKEN=true] RAW QR TOKEN (Confidential â€” Development use only):");
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
        Console.WriteLine("Commands: migrate | status | seed-demo | seed-verified-showcase | sync-tmdb-movie <id> | sync-imdb-ratings <tsv> | seed-checkin-demo | smoke | race | late | expire | verify");
        Console.WriteLine("Environment: ConnectionStrings__BetaDb (required for commands)");
        Console.WriteLine("seed-demo / seed-verified-showcase: BGP_ALLOW_DEMO_SEED=true; BGP_DEMO_PASSWORD=... (>=12 chars)");
        Console.WriteLine("sync-tmdb-movie: optional BGP_TMDB_READ_TOKEN (v4 Bearer)");
        Console.WriteLine("smoke: BGP_MOCK_HMAC_KEY_B64 and BGP_QR_AES_KEY_B64 (32 random bytes each)");
    }

    private const string OwnershipExtendedPropertyName = "BGP_TestRunOwnershipToken";

    private static string ResolveE2eMasterConnection()
    {
        var master = Environment.GetEnvironmentVariable("BGP_TEST_SQL_MASTER_CONNECTION")
            ?? "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;";
        var builder = new SqlConnectionStringBuilder(master) { InitialCatalog = "master" };
        return builder.ConnectionString;
    }

    private static async Task<int> CreateE2eDatabaseAsync(string dbName, string token)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(dbName, @"^BetaGiaiPhong_E2E_[a-zA-Z0-9_]{1,40}$"))
        {
            Console.Error.WriteLine($"Safety violation: E2E database name '{dbName}' must start with 'BetaGiaiPhong_E2E_'.");
            return 2;
        }
        var masterConnStr = ResolveE2eMasterConnection();
        await using (var masterConn = new SqlConnection(masterConnStr))
        {
            await masterConn.OpenAsync();
            await using (var checkCmd = masterConn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END;";
                checkCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = dbName });
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) == 1;
                if (exists)
                {
                    Console.Error.WriteLine($"Safety violation: E2E database '{dbName}' already exists.");
                    return 3;
                }
            }
            await using (var createCmd = masterConn.CreateCommand())
            {
                createCmd.CommandText = $"CREATE DATABASE [{dbName}];";
                await createCmd.ExecuteNonQueryAsync();
            }
            await using (var propCmd = masterConn.CreateCommand())
            {
                propCmd.CommandText = $@"
ALTER DATABASE [{dbName}] SET RECOVERY SIMPLE;
EXEC [{dbName}].sys.sp_addextendedproperty @name = N'{OwnershipExtendedPropertyName}', @value = @token;";
                propCmd.Parameters.Add(new SqlParameter("@token", SqlDbType.NVarChar, 128) { Value = token });
                await propCmd.ExecuteNonQueryAsync();
            }
        }

        var dbConnBuilder = new SqlConnectionStringBuilder(masterConnStr) { InitialCatalog = dbName };
        var options = new DbContextOptionsBuilder<BetaDbContext>()
            .UseSqlServer(dbConnBuilder.ConnectionString, sql => sql.MigrationsAssembly(typeof(BetaDbContext).Assembly.FullName))
            .Options;
        await using var db = new BetaDbContext(options);
        await db.Database.MigrateAsync();
        await DemoSeeder.SeedAsync(db);
        Console.WriteLine($"E2E database '{dbName}' created, migrated, and seeded.");
        return 0;
    }

    private static async Task<int> DropE2eDatabaseAsync(string dbName, string expectedToken)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(dbName, @"^BetaGiaiPhong_E2E_[a-zA-Z0-9_]{1,40}$"))
        {
            Console.Error.WriteLine($"Safety violation: Refusing to drop database '{dbName}' without BetaGiaiPhong_E2E_ prefix.");
            return 2;
        }
        SqlConnection.ClearAllPools();
        var masterConnStr = ResolveE2eMasterConnection();
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();
        await using (var checkCmd = masterConn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END;";
            checkCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = dbName });
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) == 1;
            if (!exists) return 0;
        }
        string? actualToken;
        await using (var tokenCmd = masterConn.CreateCommand())
        {
            tokenCmd.CommandText = $@"
SELECT CAST(value AS NVARCHAR(128))
FROM [{dbName}].sys.extended_properties
WHERE class = 0 AND name = N'{OwnershipExtendedPropertyName}';";
            var scalar = await tokenCmd.ExecuteScalarAsync();
            actualToken = scalar == null || scalar == DBNull.Value ? null : Convert.ToString(scalar);
        }
        if (!string.Equals(actualToken, expectedToken, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Safety violation: Ownership token mismatch on '{dbName}'. Refusing to drop.");
            return 3;
        }
        await using (var dropCmd = masterConn.CreateCommand())
        {
            dropCmd.CommandText = $@"
ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [{dbName}];";
            await dropCmd.ExecuteNonQueryAsync();
        }
        Console.WriteLine($"E2E database '{dbName}' dropped safely.");
        return 0;
    }

    private static async Task<int> ExecuteE2eSqlAsync(string action)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__BetaDb");
        if (string.IsNullOrWhiteSpace(conn)) return 2;
        var builder = new SqlConnectionStringBuilder(conn);
        if (!builder.InitialCatalog.StartsWith("BetaGiaiPhong_E2E_", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Safety violation: e2e-sql is only allowed against BetaGiaiPhong_E2E_ databases.");
            return 3;
        }
        if (string.Equals(action, "check-db-exists", StringComparison.OrdinalIgnoreCase))
        {
            await using var masterConn = new SqlConnection(ResolveE2eMasterConnection());
            await masterConn.OpenAsync();
            await using var existsCmd = masterConn.CreateCommand();
            existsCmd.CommandText = "SELECT CASE WHEN DB_ID(@dbName) IS NOT NULL THEN 1 ELSE 0 END;";
            existsCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = builder.InitialCatalog });
            var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync());
            Console.WriteLine($"DB_EXISTS:{exists}");
            return 0;
        }
        await using var sqlConn = new SqlConnection(conn);
        await sqlConn.OpenAsync();
        await using var cmd = sqlConn.CreateCommand();
        switch (action.ToLowerInvariant())
        {
            case "revoke-staff":
                cmd.CommandText = @"
DELETE ur FROM dbo.UserRoles ur
JOIN dbo.Users u ON u.user_id = ur.user_id
JOIN dbo.Roles r ON r.role_id = ur.role_id
WHERE u.normalized_email = 'STAFF@DEMO.INVALID' AND r.role_code = 'STAFF';";
                await cmd.ExecuteNonQueryAsync();
                return 0;
            case "restore-staff":
                cmd.CommandText = @"
INSERT INTO dbo.UserRoles (user_id, role_id)
SELECT u.user_id, r.role_id
FROM dbo.Users u CROSS JOIN dbo.Roles r
WHERE u.normalized_email = 'STAFF@DEMO.INVALID' AND r.role_code = 'STAFF'
AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur WHERE ur.user_id = u.user_id AND ur.role_id = r.role_id);";
                await cmd.ExecuteNonQueryAsync();
                return 0;
            case "force-expire-order":
                var orderIdStr = Environment.GetEnvironmentVariable("BGP_E2E_ORDER_ID");
                if (!long.TryParse(orderIdStr, out var orderId)) return 2;
                cmd.CommandText = "UPDATE dbo.Orders SET created_utc = DATEADD(MINUTE, -10, SYSUTCDATETIME()), expires_utc = DATEADD(MINUTE, -2, SYSUTCDATETIME()) WHERE order_id = @orderId AND status = 'PENDING_PAYMENT'; UPDATE dbo.ShowtimeSeats SET hold_expires_utc = DATEADD(MINUTE, -2, SYSUTCDATETIME()) WHERE owner_order_id = @orderId AND state = 'HELD';";
                cmd.Parameters.Add(new SqlParameter("@orderId", SqlDbType.BigInt) { Value = orderId });
                await cmd.ExecuteNonQueryAsync();
                return 0;
            case "set-order-remaining-seconds":
                var remOrderIdStr = Environment.GetEnvironmentVariable("BGP_E2E_ORDER_ID");
                var remSecondsStr = Environment.GetEnvironmentVariable("BGP_E2E_REMAINING_SECONDS") ?? "4";
                if (!long.TryParse(remOrderIdStr, out var remOrderId) || !int.TryParse(remSecondsStr, out var remSec) || remSec is < 1 or > 300) return 2;
                cmd.CommandText = "UPDATE dbo.Orders SET created_utc = DATEADD(MINUTE, -10, SYSUTCDATETIME()), expires_utc = DATEADD(SECOND, @sec, SYSUTCDATETIME()) WHERE order_id = @orderId AND status = 'PENDING_PAYMENT'; UPDATE dbo.ShowtimeSeats SET hold_expires_utc = DATEADD(SECOND, @sec, SYSUTCDATETIME()) WHERE owner_order_id = @orderId AND state = 'HELD';";
                cmd.Parameters.Add(new SqlParameter("@orderId", SqlDbType.BigInt) { Value = remOrderId });
                cmd.Parameters.Add(new SqlParameter("@sec", SqlDbType.Int) { Value = remSec });
                await cmd.ExecuteNonQueryAsync();
                return 0;
            case "set-order-needs-review":
                var revOrderIdStr = Environment.GetEnvironmentVariable("BGP_E2E_ORDER_ID");
                if (!long.TryParse(revOrderIdStr, out var revOrderId)) return 2;
                cmd.CommandText = "UPDATE dbo.ShowtimeSeats SET state = 'AVAILABLE', owner_order_id = NULL, hold_expires_utc = NULL WHERE owner_order_id = @orderId AND state = 'HELD'; UPDATE dbo.Orders SET status = 'NEEDS_REVIEW', paid_utc = NULL, created_utc = DATEADD(MINUTE, -10, SYSUTCDATETIME()), expires_utc = DATEADD(MINUTE, -5, SYSUTCDATETIME()) WHERE order_id = @orderId AND status IN ('PENDING_PAYMENT', 'EXPIRED', 'CANCELLED', 'FAILED');";
                cmd.Parameters.Add(new SqlParameter("@orderId", SqlDbType.BigInt) { Value = revOrderId });
                await cmd.ExecuteNonQueryAsync();
                return 0;
            case "get-order-ciphers":
                var cipherOrderIdStr = Environment.GetEnvironmentVariable("BGP_E2E_ORDER_ID");
                if (!long.TryParse(cipherOrderIdStr, out var cipherOrderId)) return 2;
                cmd.CommandText = "SELECT qr_token_cipher FROM dbo.Tickets WHERE order_id = @orderId ORDER BY ticket_id;";
                cmd.Parameters.Add(new SqlParameter("@orderId", SqlDbType.BigInt) { Value = cipherOrderId });
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var cipherBytes = (byte[])reader[0];
                        Console.WriteLine("CIPHER_B64:" + Convert.ToBase64String(cipherBytes));
                    }
                }
                return 0;
            case "corrupt-e2e-db":
                cmd.CommandText = "UPDATE TOP (1) dbo.Movies SET status = 'ARCHIVED' WHERE movie_id IN (SELECT movie_id FROM dbo.Showtimes WHERE status = 'ON_SALE' AND start_utc > SYSUTCDATETIME());";
                await cmd.ExecuteNonQueryAsync();
                return 0;
            case "seed-admin":
                cmd.CommandText = @"
DECLARE @adminRoleId int;
SELECT @adminRoleId = role_id FROM dbo.Roles WHERE role_code = 'ADMIN';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE normalized_email = 'ADMIN@DEMO.INVALID')
BEGIN
    INSERT INTO dbo.Users (email, normalized_email, display_name, status, created_utc, password_hash)
    SELECT 'admin@demo.invalid', 'ADMIN@DEMO.INVALID', N'Demo Quan tri', 'ACTIVE', SYSUTCDATETIME(), password_hash
    FROM dbo.Users WHERE normalized_email = 'MANAGER@DEMO.INVALID';

    DECLARE @adminId bigint = SCOPE_IDENTITY();

    INSERT INTO dbo.UserRoles (user_id, role_id)
    VALUES (@adminId, @adminRoleId);
END";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("ADMIN_SEEDED");
                return 0;
            case "seed-empty-customer":
                cmd.CommandText = @"
DECLARE @custRoleId int;
SELECT @custRoleId = role_id FROM dbo.Roles WHERE role_code = 'CUSTOMER';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE normalized_email = 'CUSTOMER3@DEMO.INVALID')
BEGIN
    INSERT INTO dbo.Users (email, normalized_email, display_name, status, created_utc, password_hash)
    SELECT 'customer3@demo.invalid', 'CUSTOMER3@DEMO.INVALID', N'Demo Khách hàng Mới', 'ACTIVE', SYSUTCDATETIME(), password_hash
    FROM dbo.Users WHERE normalized_email = 'CUSTOMER1@DEMO.INVALID';

    DECLARE @cust3Id bigint = SCOPE_IDENTITY();

    INSERT INTO dbo.UserRoles (user_id, role_id)
    VALUES (@cust3Id, @custRoleId);
END";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("EMPTY_CUSTOMER_SEEDED");
                return 0;
            case "seed-ui-fixtures":
                cmd.CommandText = @"
IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__E2eMovieSnapshots (
        snapshot_scope NVARCHAR(64) NOT NULL,
        movie_id BIGINT NOT NULL,
        title NVARCHAR(250) NOT NULL,
        duration_min SMALLINT NOT NULL,
        genre_text NVARCHAR(160) NULL,
        status VARCHAR(12) NOT NULL,
        age_label NVARCHAR(24) NULL,
        synopsis NVARCHAR(MAX) NULL,
        poster_url NVARCHAR(500) NULL,
        is_inserted_fixture BIT NOT NULL DEFAULT 0,
        PRIMARY KEY (snapshot_scope, movie_id)
    );
END

IF OBJECT_ID('dbo.__E2eShowtimeFixtures', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__E2eShowtimeFixtures (
        fixture_scope NVARCHAR(64) NOT NULL,
        showtime_id BIGINT NOT NULL,
        PRIMARY KEY (fixture_scope, showtime_id)
    );
END

-- If ui-fixtures scope already exists, clean it first for idempotency
IF EXISTS (SELECT 1 FROM dbo.__E2eShowtimeFixtures WHERE fixture_scope = 'ui-fixtures')
BEGIN
    DELETE sp FROM dbo.SeatPrices sp JOIN dbo.__E2eShowtimeFixtures sf ON sp.showtime_id = sf.showtime_id WHERE sf.fixture_scope = 'ui-fixtures';
    DELETE ss FROM dbo.ShowtimeSeats ss JOIN dbo.__E2eShowtimeFixtures sf ON ss.showtime_id = sf.showtime_id WHERE sf.fixture_scope = 'ui-fixtures';
    DELETE st FROM dbo.Showtimes st JOIN dbo.__E2eShowtimeFixtures sf ON st.showtime_id = sf.showtime_id WHERE sf.fixture_scope = 'ui-fixtures';
    DELETE FROM dbo.__E2eShowtimeFixtures WHERE fixture_scope = 'ui-fixtures';
END

IF EXISTS (SELECT 1 FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'ui-fixtures')
BEGIN
    UPDATE m
    SET m.title = s.title, m.duration_min = s.duration_min, m.genre_text = s.genre_text,
        m.status = s.status, m.age_label = s.age_label, m.synopsis = s.synopsis, m.poster_url = s.poster_url
    FROM dbo.Movies m JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
    WHERE s.snapshot_scope = 'ui-fixtures' AND s.is_inserted_fixture = 0;

    DELETE m FROM dbo.Movies m JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
    WHERE s.snapshot_scope = 'ui-fixtures' AND s.is_inserted_fixture = 1;

    DELETE FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'ui-fixtures';
END

-- 1. Movie with 0 showtimes, null synopsis, and target broken poster URL for fallback test
DECLARE @noShowtimeMovieId BIGINT;
INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
VALUES (N'Phim Khong Co Suat', 90, N'Tâm Lý', 'PUBLISHED', 'P', NULL, 'https://example.invalid/posters/broken-poster-target.jpg');
SET @noShowtimeMovieId = SCOPE_IDENTITY();
INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'ui-fixtures', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 1 FROM dbo.Movies WHERE movie_id = @noShowtimeMovieId;

-- 2. Movie with long title, long synopsis, and multi-date showtimes (including UTC+7 midnight crossover & sold-out showtime)
DECLARE @multiMovieId BIGINT;
INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
VALUES (
    N'Chien Binh Khong Gian - Vu Tru Huyen Thoai Dao Chieu Dot Pha Thoi Gian Khong Hoi Ket',
    135,
    N'Hành Động, Viễn Tưởng',
    'PUBLISHED',
    'T16',
    N'Chiến trường đa vũ trụ rực lửa mở ra cuộc đối đầu định mệnh giữa các nền văn minh cổ đại và hạm đội tuần tra liên ngân hà. Một hành trình nghẹt thở với kỹ xảo điện ảnh hoành tráng và âm thanh sống động tại phòng chiếu tiêu chuẩn.',
    '/media/movies/verified/tears-of-steel-2012-poster-w342.jpg'
);
SET @multiMovieId = SCOPE_IDENTITY();
INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'ui-fixtures', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 1 FROM dbo.Movies WHERE movie_id = @multiMovieId;

-- 3. Movie with missing poster (NULL) and 0 showtimes
DECLARE @m3Id BIGINT;
INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
VALUES (N'Thanh Pho Hoang Hon', 105, N'Lãng Mạn', 'PUBLISHED', 'T13', N'Chuyện tình đô thị nhẹ nhàng.', NULL);
SET @m3Id = SCOPE_IDENTITY();
INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'ui-fixtures', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 1 FROM dbo.Movies WHERE movie_id = @m3Id;

-- 4. Additional movie 4
DECLARE @m4Id BIGINT;
INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
VALUES (N'Vu Dieu Bong Dem', 115, N'Kinh Dị', 'PUBLISHED', 'T18', N'Bóng ma ẩn sâu sau bức màn nhung.', '/media/movies/verified/notld-1968-poster-w342.jpg');
SET @m4Id = SCOPE_IDENTITY();
INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'ui-fixtures', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 1 FROM dbo.Movies WHERE movie_id = @m4Id;

-- 5. Additional movie 5
DECLARE @m5Id BIGINT;
INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
VALUES (N'Sieu Diep Vien Nhi', 88, N'Hoạt Hình', 'PUBLISHED', 'P', N'Biệt đội thám tử nhí phá án tài ba.', '/media/movies/verified/spring-2019-poster-w342.jpg');
SET @m5Id = SCOPE_IDENTITY();
INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'ui-fixtures', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 1 FROM dbo.Movies WHERE movie_id = @m5Id;

-- 6. Pre-existing DRAFT movie (must never be published by restore-all-movies and must not appear publicly)
DECLARE @draftMovieId BIGINT;
INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
VALUES (N'Phim Ban Nhap Chua Cong Bo', 100, N'Tài Liệu', 'DRAFT', 'T13', N'Bản nháp nội bộ chưa công bố.', NULL);
SET @draftMovieId = SCOPE_IDENTITY();
INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'ui-fixtures', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 1 FROM dbo.Movies WHERE movie_id = @draftMovieId;

-- Seed multi-date showtimes for @multiMovieId across 2 Vietnam dates (UTC+7)
DECLARE @roomId BIGINT = (SELECT TOP (1) room_id FROM dbo.Rooms WHERE status = 'ACTIVE' ORDER BY room_id);
DECLARE @creatorId BIGINT = (SELECT TOP (1) user_id FROM dbo.Users WHERE normalized_email = 'MANAGER@DEMO.INVALID');
DECLARE @cap INT = (SELECT COUNT(*) FROM dbo.Seats WHERE room_id = @roomId AND is_active = 1);

IF @roomId IS NOT NULL AND @creatorId IS NOT NULL
BEGIN
    DECLARE @nowVn DATETIME2(3) = DATEADD(HOUR, 7, SYSUTCDATETIME());
    DECLARE @day1Vn DATETIME2(3) = DATEADD(DAY, 1, CAST(CAST(@nowVn AS DATE) AS DATETIME2(3)));
    DECLARE @day2Vn DATETIME2(3) = DATEADD(DAY, 2, CAST(CAST(@nowVn AS DATE) AS DATETIME2(3)));

    -- Showtime A: Day 1 VN at 18:30 -> 20:45 VN (Available seats)
    DECLARE @stAStartUtc DATETIME2(3) = DATEADD(HOUR, -7, DATEADD(MINUTE, 18 * 60 + 30, @day1Vn));
    DECLARE @stAEndUtc DATETIME2(3) = DATEADD(MINUTE, 135, @stAStartUtc);
    INSERT INTO dbo.Showtimes (movie_id, room_id, start_utc, end_utc, buffer_minutes, status, sellable_capacity_snapshot, created_by, created_utc)
    VALUES (@multiMovieId, @roomId, @stAStartUtc, @stAEndUtc, 15, 'ON_SALE', @cap, @creatorId, SYSUTCDATETIME());
    DECLARE @stAId BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.__E2eShowtimeFixtures (fixture_scope, showtime_id) VALUES ('ui-fixtures', @stAId);
    INSERT INTO dbo.SeatPrices (showtime_id, seat_type_code, amount_vnd) VALUES (@stAId, 'STANDARD', 75000), (@stAId, 'VIP', 95000);
    INSERT INTO dbo.ShowtimeSeats (showtime_id, room_id, seat_id, state)
    SELECT @stAId, @roomId, seat_id, CASE WHEN is_active = 1 THEN 'AVAILABLE' ELSE 'BLOCKED' END FROM dbo.Seats WHERE room_id = @roomId;

    -- Showtime B: Day 1 VN at 23:15 -> 01:30 (+1 day) VN (Crosses midnight in UTC+7, 0 available seats)
    DECLARE @stBStartUtc DATETIME2(3) = DATEADD(HOUR, -7, DATEADD(MINUTE, 23 * 60 + 15, @day1Vn));
    DECLARE @stBEndUtc DATETIME2(3) = DATEADD(MINUTE, 135, @stBStartUtc);
    INSERT INTO dbo.Showtimes (movie_id, room_id, start_utc, end_utc, buffer_minutes, status, sellable_capacity_snapshot, created_by, created_utc)
    VALUES (@multiMovieId, @roomId, @stBStartUtc, @stBEndUtc, 15, 'ON_SALE', @cap, @creatorId, SYSUTCDATETIME());
    DECLARE @stBId BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.__E2eShowtimeFixtures (fixture_scope, showtime_id) VALUES ('ui-fixtures', @stBId);
    INSERT INTO dbo.SeatPrices (showtime_id, seat_type_code, amount_vnd) VALUES (@stBId, 'STANDARD', 75000), (@stBId, 'VIP', 95000);
    INSERT INTO dbo.ShowtimeSeats (showtime_id, room_id, seat_id, state)
    SELECT @stBId, @roomId, seat_id, 'BLOCKED' FROM dbo.Seats WHERE room_id = @roomId;

    -- Showtime C: Day 2 VN at 00:30 -> 02:45 VN (17:30 UTC previous day - tests UTC+7 date boundary)
    DECLARE @stCStartUtc DATETIME2(3) = DATEADD(HOUR, -7, DATEADD(MINUTE, 30, @day2Vn));
    DECLARE @stCEndUtc DATETIME2(3) = DATEADD(MINUTE, 135, @stCStartUtc);
    INSERT INTO dbo.Showtimes (movie_id, room_id, start_utc, end_utc, buffer_minutes, status, sellable_capacity_snapshot, created_by, created_utc)
    VALUES (@multiMovieId, @roomId, @stCStartUtc, @stCEndUtc, 15, 'ON_SALE', @cap, @creatorId, SYSUTCDATETIME());
    DECLARE @stCId BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.__E2eShowtimeFixtures (fixture_scope, showtime_id) VALUES ('ui-fixtures', @stCId);
    INSERT INTO dbo.SeatPrices (showtime_id, seat_type_code, amount_vnd) VALUES (@stCId, 'STANDARD', 75000), (@stCId, 'VIP', 95000);
    INSERT INTO dbo.ShowtimeSeats (showtime_id, room_id, seat_id, state)
    SELECT @stCId, @roomId, seat_id, CASE WHEN is_active = 1 THEN 'AVAILABLE' ELSE 'BLOCKED' END FROM dbo.Seats WHERE room_id = @roomId;

    -- Showtime D: Day 2 VN at 19:00 -> 21:15 VN (Available seats)
    DECLARE @stDStartUtc DATETIME2(3) = DATEADD(HOUR, -7, DATEADD(MINUTE, 19 * 60, @day2Vn));
    DECLARE @stDEndUtc DATETIME2(3) = DATEADD(MINUTE, 135, @stDStartUtc);
    INSERT INTO dbo.Showtimes (movie_id, room_id, start_utc, end_utc, buffer_minutes, status, sellable_capacity_snapshot, created_by, created_utc)
    VALUES (@multiMovieId, @roomId, @stDStartUtc, @stDEndUtc, 15, 'ON_SALE', @cap, @creatorId, SYSUTCDATETIME());
    DECLARE @stDId BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.__E2eShowtimeFixtures (fixture_scope, showtime_id) VALUES ('ui-fixtures', @stDId);
    INSERT INTO dbo.SeatPrices (showtime_id, seat_type_code, amount_vnd) VALUES (@stDId, 'STANDARD', 75000), (@stDId, 'VIP', 95000);
    INSERT INTO dbo.ShowtimeSeats (showtime_id, room_id, seat_id, state)
    SELECT @stDId, @roomId, seat_id, CASE WHEN is_active = 1 THEN 'AVAILABLE' ELSE 'BLOCKED' END FROM dbo.Seats WHERE room_id = @roomId;
END

SELECT @draftMovieId AS DraftMovieId, @multiMovieId AS MultiMovieId, @noShowtimeMovieId AS NoShowtimeMovieId;
";
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Console.WriteLine($"DRAFT_MOVIE_ID:{reader["DraftMovieId"]}");
                        Console.WriteLine($"MULTI_DATE_MOVIE_ID:{reader["MultiMovieId"]}");
                        Console.WriteLine($"NO_SHOWTIME_MOVIE_ID:{reader["NoShowtimeMovieId"]}");
                    }
                }
                Console.WriteLine("UI_FIXTURES_SEEDED");
                return 0;
            case "reset-ui-fixtures":
                cmd.CommandText = @"
IF OBJECT_ID('dbo.__E2eShowtimeFixtures', 'U') IS NOT NULL
BEGIN
    DELETE sp FROM dbo.SeatPrices sp
    JOIN dbo.__E2eShowtimeFixtures sf ON sp.showtime_id = sf.showtime_id
    WHERE sf.fixture_scope = 'ui-fixtures';

    DELETE ss FROM dbo.ShowtimeSeats ss
    JOIN dbo.__E2eShowtimeFixtures sf ON ss.showtime_id = sf.showtime_id
    WHERE sf.fixture_scope = 'ui-fixtures';

    DELETE st FROM dbo.Showtimes st
    JOIN dbo.__E2eShowtimeFixtures sf ON st.showtime_id = sf.showtime_id
    WHERE sf.fixture_scope = 'ui-fixtures';

    DELETE FROM dbo.__E2eShowtimeFixtures WHERE fixture_scope = 'ui-fixtures';
END

IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NOT NULL
BEGIN
    UPDATE m
    SET m.title = s.title,
        m.duration_min = s.duration_min,
        m.genre_text = s.genre_text,
        m.status = s.status,
        m.age_label = s.age_label,
        m.synopsis = s.synopsis,
        m.poster_url = s.poster_url
    FROM dbo.Movies m
    JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
    WHERE s.snapshot_scope = 'ui-fixtures' AND s.is_inserted_fixture = 0;

    DELETE m
    FROM dbo.Movies m
    JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
    WHERE s.snapshot_scope = 'ui-fixtures' AND s.is_inserted_fixture = 1;

    DELETE FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'ui-fixtures';
END

IF OBJECT_ID('dbo.__E2eShowtimeFixtures', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.__E2eShowtimeFixtures)
BEGIN
    DROP TABLE dbo.__E2eShowtimeFixtures;
END

IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.__E2eMovieSnapshots)
BEGIN
    DROP TABLE dbo.__E2eMovieSnapshots;
END";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("UI_FIXTURES_RESET");
                return 0;
            case "hide-all-movies":
                cmd.CommandText = @"
IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__E2eMovieSnapshots (
        snapshot_scope NVARCHAR(64) NOT NULL,
        movie_id BIGINT NOT NULL,
        title NVARCHAR(250) NOT NULL,
        duration_min SMALLINT NOT NULL,
        genre_text NVARCHAR(160) NULL,
        status VARCHAR(12) NOT NULL,
        age_label NVARCHAR(24) NULL,
        synopsis NVARCHAR(MAX) NULL,
        poster_url NVARCHAR(500) NULL,
        is_inserted_fixture BIT NOT NULL DEFAULT 0,
        PRIMARY KEY (snapshot_scope, movie_id)
    );
END

DELETE FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'hide-all';

INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
SELECT 'hide-all', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 0
FROM dbo.Movies
WHERE status = 'PUBLISHED';

UPDATE m
SET m.status = 'DRAFT'
FROM dbo.Movies m
JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
WHERE s.snapshot_scope = 'hide-all';";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("ALL_MOVIES_HIDDEN");
                return 0;
            case "restore-all-movies":
                cmd.CommandText = @"
IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NOT NULL
BEGIN
    UPDATE m
    SET m.status = s.status
    FROM dbo.Movies m
    JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
    WHERE s.snapshot_scope = 'hide-all';

    DELETE FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'hide-all';

    IF NOT EXISTS (SELECT 1 FROM dbo.__E2eMovieSnapshots)
    BEGIN
        DROP TABLE dbo.__E2eMovieSnapshots;
    END
END";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("ALL_MOVIES_RESTORED");
                return 0;
            case "set-long-title":
                cmd.CommandText = @"
IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__E2eMovieSnapshots (
        snapshot_scope NVARCHAR(64) NOT NULL,
        movie_id BIGINT NOT NULL,
        title NVARCHAR(250) NOT NULL,
        duration_min SMALLINT NOT NULL,
        genre_text NVARCHAR(160) NULL,
        status VARCHAR(12) NOT NULL,
        age_label NVARCHAR(24) NULL,
        synopsis NVARCHAR(MAX) NULL,
        poster_url NVARCHAR(500) NULL,
        is_inserted_fixture BIT NOT NULL DEFAULT 0,
        PRIMARY KEY (snapshot_scope, movie_id)
    );
END

DELETE FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'long-title';

DECLARE @heroTargetId BIGINT;
SELECT TOP (1) @heroTargetId = m.movie_id
FROM dbo.Movies m
WHERE m.status = 'PUBLISHED'
  AND m.backdrop_url IS NOT NULL
  AND m.poster_url IS NOT NULL
  AND m.poster_url NOT LIKE '%.svg'
  AND ISNULL(m.artwork_source, '') NOT IN ('NONE', 'PENDING_TMDB_API_KEY')
  AND m.synopsis IS NOT NULL
ORDER BY CASE WHEN EXISTS (SELECT 1 FROM dbo.Showtimes st WHERE st.movie_id = m.movie_id AND st.status = 'ON_SALE' AND st.start_utc > SYSUTCDATETIME()) THEN 0 ELSE 1 END, m.title, m.movie_id;

IF @heroTargetId IS NOT NULL
BEGIN
    INSERT INTO dbo.__E2eMovieSnapshots (snapshot_scope, movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, is_inserted_fixture)
    SELECT 'long-title', movie_id, title, duration_min, genre_text, status, age_label, synopsis, poster_url, 0
    FROM dbo.Movies
    WHERE movie_id = @heroTargetId;

    UPDATE dbo.Movies
    SET title = N'Chien Binh Khong Gian - Vu Tru Huyen Thoai Dao Chieu Dot Pha Thoi Gian Khong Hoi Ket (Ban Chieu Rap 3D Dac Biet IMAX)'
    WHERE movie_id = @heroTargetId;
END";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("LONG_TITLE_SET");
                return 0;
            case "restore-title":
                cmd.CommandText = @"
IF OBJECT_ID('dbo.__E2eMovieSnapshots', 'U') IS NOT NULL
BEGIN
    UPDATE m
    SET m.title = s.title
    FROM dbo.Movies m
    JOIN dbo.__E2eMovieSnapshots s ON m.movie_id = s.movie_id
    WHERE s.snapshot_scope = 'long-title';

    DELETE FROM dbo.__E2eMovieSnapshots WHERE snapshot_scope = 'long-title';

    IF NOT EXISTS (SELECT 1 FROM dbo.__E2eMovieSnapshots)
    BEGIN
        DROP TABLE dbo.__E2eMovieSnapshots;
    END
END";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("TITLE_RESTORED");
                return 0;
            case "inspect-movies-state":
                cmd.CommandText = "SELECT movie_id, status, title, ISNULL(age_label, 'NULL') AS age_label, ISNULL(poster_url, 'NULL') AS poster_url, CASE WHEN synopsis IS NULL THEN 'NULL' ELSE 'SET' END AS synopsis_state FROM dbo.Movies ORDER BY movie_id;";
                await using (var stateReader = await cmd.ExecuteReaderAsync())
                {
                    while (await stateReader.ReadAsync())
                    {
                        Console.WriteLine($"MOVIE_ROW|{stateReader["movie_id"]}|{stateReader["status"]}|{stateReader["title"]}|{stateReader["age_label"]}|{stateReader["poster_url"]}|{stateReader["synopsis_state"]}");
                    }
                }
                return 0;
            default:
                Console.Error.WriteLine($"Unknown e2e-sql action: {action}");
                return 2;
        }
    }

}
