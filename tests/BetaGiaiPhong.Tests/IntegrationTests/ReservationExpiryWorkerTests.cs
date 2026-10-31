using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Services.Workers;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class ReservationExpiryWorkerTests(TestDatabaseFixture fixture)
{
    private async Task<(long ShowtimeId, long OrderId, List<long> SeatIds)> CreateOrderAsync(
        string status, DateTime expiresUtc)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.FirstAsync();
        var room = await db.Rooms.FirstAsync();
        var manager = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customer = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(1).AddMinutes(120),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = manager.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seats = await db.Seats.Where(s => s.RoomId == room.RoomId && s.IsActive).Take(2).ToListAsync();
        var seatIds = seats.Select(s => s.SeatId).ToList();

        foreach (var seat in seats)
        {
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = seat.SeatId,
                RoomId = room.RoomId,
                State = SeatStates.Available
            });
        }
        foreach (var st in await db.SeatTypes.ToListAsync())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st.SeatTypeCode,
                AmountVnd = 75000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var res = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customer.UserId, showtime.ShowtimeId, seatIds);

        var order = await db.Orders.SingleAsync(o => o.OrderId == res.OrderId);
        order.CreatedUtc = expiresUtc.AddMinutes(-15);
        order.ExpiresUtc = expiresUtc;
        order.Status = status;

        if (status == OrderStates.Paid)
        {
            order.PaidUtc = expiresUtc.AddMinutes(-5);
            var holds = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtime.ShowtimeId && s.OwnerOrderId == order.OrderId).ToListAsync();
            var qr = new QrTokenProtector();
            foreach (var h in holds)
            {
                h.State = SeatStates.Sold;
                h.HoldExpiresUtc = null;
                var tok = qr.Issue();
                db.Tickets.Add(new Ticket
                {
                    TicketCode = "TKT-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    OrderId = order.OrderId,
                    ShowtimeId = order.ShowtimeId,
                    SeatId = h.SeatId,
                    Status = "ISSUED",
                    QrTokenHash = tok.Hash,
                    QrTokenCipher = tok.Cipher,
                    QrKeyId = qr.KeyId,
                    IssuedUtc = expiresUtc.AddMinutes(-5)
                });
            }
        }
        else if (status is OrderStates.Cancelled or OrderStates.Expired or OrderStates.Failed or OrderStates.Review)
        {
            var holds = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtime.ShowtimeId && s.OwnerOrderId == order.OrderId).ToListAsync();
            foreach (var h in holds)
            {
                h.State = SeatStates.Available;
                h.OwnerOrderId = null;
                h.HoldExpiresUtc = null;
            }
        }
        else
        {
            var holds = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtime.ShowtimeId && s.OwnerOrderId == order.OrderId).ToListAsync();
            foreach (var h in holds)
            {
                h.HoldExpiresUtc = expiresUtc;
            }
        }

        await db.SaveChangesAsync();

        return (showtime.ShowtimeId, order.OrderId, seatIds);
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BetaDbContext>(options => options.UseSqlServer(fixture.ConnectionString));
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<OrderLifecycleService>();
        services.AddScoped<PaymentService>();
        services.AddSingleton<QrTokenProtector>();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BetaGiaiPhong.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task TC_WORKER_01_DisabledWorker_DoesNotExecute()
    {
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BGP_ENABLE_EXPIRY_WORKER"] = "false"
            })
            .Build();

        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.ExecuteTask?.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TC_WORKER_02_SweepsOverduePendingReservations_MarksExpiredAndReleasesSeats()
    {
        // Create an overdue reservation (expired 10 minutes ago)
        var pastUtc = DateTime.UtcNow.AddMinutes(-10);
        var (showtimeId, orderId, seatIds) = await CreateOrderAsync(OrderStates.Pending, pastUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        var expiredCount = await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);
        Assert.True(expiredCount >= 1);

        // Verify order status transitioned to EXPIRED
        await using var db = await fixture.CreateDbContextAsync();
        var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
        Assert.Equal(OrderStates.Expired, order.Status);

        // Verify seats were released back to AVAILABLE
        var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId && seatIds.Contains(s.SeatId)).ToListAsync();
        Assert.All(seats, s =>
        {
            Assert.Equal(SeatStates.Available, s.State);
            Assert.Null(s.OwnerOrderId);
            Assert.Null(s.HoldExpiresUtc);
        });

        // Verify AuditLog written
        var audit = await db.AuditLogs.AnyAsync(a => a.EventType == "ORDER_EXPIRED" && a.EntityKey == orderId.ToString());
        Assert.True(audit);
    }

    [Fact]
    public async Task TC_WORKER_03_PreservesNonOverduePendingOrders()
    {
        // Create a pending order with 10 minutes left
        var futureUtc = DateTime.UtcNow.AddMinutes(10);
        var (showtimeId, orderId, seatIds) = await CreateOrderAsync(OrderStates.Pending, futureUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);

        // Verify order is STILL PENDING
        await using var db = await fixture.CreateDbContextAsync();
        var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
        Assert.Equal(OrderStates.Pending, order.Status);

        // Verify seats are STILL HELD
        var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId && seatIds.Contains(s.SeatId)).ToListAsync();
        Assert.All(seats, s =>
        {
            Assert.Equal(SeatStates.Held, s.State);
            Assert.Equal(orderId, s.OwnerOrderId);
        });
    }

    [Fact]
    public async Task TC_WORKER_04_PreservesPaidCancelledAndNeedsReviewOrders()
    {
        var pastUtc = DateTime.UtcNow.AddMinutes(-10);

        var (paidStId, paidOrderId, paidSeats) = await CreateOrderAsync(OrderStates.Paid, pastUtc);
        var (cancelledStId, cancelledOrderId, cancelledSeats) = await CreateOrderAsync(OrderStates.Cancelled, pastUtc);
        var (failedStId, failedOrderId, failedSeats) = await CreateOrderAsync(OrderStates.Failed, pastUtc);
        var (reviewStId, reviewOrderId, reviewSeats) = await CreateOrderAsync(OrderStates.Review, pastUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);

        await using var db = await fixture.CreateDbContextAsync();

        // 1. Verify PAID order preserved, tickets intact, seats SOLD
        var paidOrder = await db.Orders.SingleAsync(o => o.OrderId == paidOrderId);
        Assert.Equal(OrderStates.Paid, paidOrder.Status);
        var paidTickets = await db.Tickets.Where(t => t.OrderId == paidOrderId).ToListAsync();
        Assert.Equal(paidSeats.Count, paidTickets.Count);
        var paidSeatsDb = await db.ShowtimeSeats.Where(s => s.ShowtimeId == paidStId && paidSeats.Contains(s.SeatId)).ToListAsync();
        Assert.All(paidSeatsDb, s => Assert.Equal(SeatStates.Sold, s.State));

        // 2. Verify CANCELLED order preserved, seats AVAILABLE
        var cancelledOrder = await db.Orders.SingleAsync(o => o.OrderId == cancelledOrderId);
        Assert.Equal(OrderStates.Cancelled, cancelledOrder.Status);
        var cancelledSeatsDb = await db.ShowtimeSeats.Where(s => s.ShowtimeId == cancelledStId && cancelledSeats.Contains(s.SeatId)).ToListAsync();
        Assert.All(cancelledSeatsDb, s =>
        {
            Assert.Equal(SeatStates.Available, s.State);
            Assert.Null(s.OwnerOrderId);
            Assert.Null(s.HoldExpiresUtc);
        });

        // 3. Verify FAILED order preserved, seats AVAILABLE
        var failedOrder = await db.Orders.SingleAsync(o => o.OrderId == failedOrderId);
        Assert.Equal(OrderStates.Failed, failedOrder.Status);
        var failedSeatsDb = await db.ShowtimeSeats.Where(s => s.ShowtimeId == failedStId && failedSeats.Contains(s.SeatId)).ToListAsync();
        Assert.All(failedSeatsDb, s =>
        {
            Assert.Equal(SeatStates.Available, s.State);
            Assert.Null(s.OwnerOrderId);
            Assert.Null(s.HoldExpiresUtc);
        });

        // 4. Verify NEEDS_REVIEW order preserved, seats AVAILABLE
        var reviewOrder = await db.Orders.SingleAsync(o => o.OrderId == reviewOrderId);
        Assert.Equal(OrderStates.Review, reviewOrder.Status);
        var reviewSeatsDb = await db.ShowtimeSeats.Where(s => s.ShowtimeId == reviewStId && reviewSeats.Contains(s.SeatId)).ToListAsync();
        Assert.All(reviewSeatsDb, s =>
        {
            Assert.Equal(SeatStates.Available, s.State);
            Assert.Null(s.OwnerOrderId);
            Assert.Null(s.HoldExpiresUtc);
        });
    }

    [Fact]
    public async Task TC_WORKER_05_BatchProcessing_RespectsBatchLimit()
    {
        var pastUtc = DateTime.UtcNow.AddMinutes(-15);
        var orderIds = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var (_, oId, _) = await CreateOrderAsync(OrderStates.Pending, pastUtc.AddSeconds(i));
            orderIds.Add(oId);
        }

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        // Process with batch limit of 1
        var batch1 = await worker.ProcessExpiredOrdersAsync(1, CancellationToken.None);
        Assert.Equal(1, batch1);

        // Process second batch of 1
        var batch2 = await worker.ProcessExpiredOrdersAsync(1, CancellationToken.None);
        Assert.Equal(1, batch2);

        // Process remaining batch of 100
        var batch3 = await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);
        Assert.True(batch3 >= 1);

        // Verify all 3 test orders are now expired
        await using var db = await fixture.CreateDbContextAsync();
        var statuses = await db.Orders.Where(o => orderIds.Contains(o.OrderId)).Select(o => o.Status).ToListAsync();
        Assert.All(statuses, s => Assert.Equal(OrderStates.Expired, s));
    }

    private async Task<(long ShowtimeId, long OrderId, long SeatId, long PaymentId, string AttemptKey, string OrderCode, long TotalVnd)> CreateOrderWithPaymentAttemptAsync(DateTime expiresUtc)
    {
        await using var initDb = await fixture.CreateDbContextAsync();
        var movie = await initDb.Movies.FirstAsync();
        var room = await initDb.Rooms.FirstAsync();
        var manager = await initDb.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customer = await initDb.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(1).AddMinutes(120),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = manager.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        initDb.Showtimes.Add(showtime);
        await initDb.SaveChangesAsync();

        var seat = await initDb.Seats.FirstAsync(s => s.RoomId == room.RoomId && s.IsActive);
        initDb.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = SeatStates.Available
        });
        foreach (var st in await initDb.SeatTypes.ToListAsync())
        {
            initDb.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st.SeatTypeCode,
                AmountVnd = 80000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await initDb.SaveChangesAsync();

        var reservation = await new SeatReservationService(initDb, new SystemClock())
            .ReserveAsync(customer.UserId, showtime.ShowtimeId, [seat.SeatId]);

        var attemptKey = "ATTEMPT-RACE-" + Guid.NewGuid().ToString("N");
        var paymentServiceInit = new PaymentService(initDb, new SystemClock(), new QrTokenProtector());
        var paymentId = await paymentServiceInit.CreateMockAttemptAsync(customer.UserId, reservation.OrderId, attemptKey);

        var order = await initDb.Orders.SingleAsync(o => o.OrderId == reservation.OrderId);
        order.CreatedUtc = expiresUtc.AddMinutes(-15);
        order.ExpiresUtc = expiresUtc;
        var hold = await initDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtime.ShowtimeId && s.SeatId == seat.SeatId);
        hold.HoldExpiresUtc = expiresUtc;
        await initDb.SaveChangesAsync();

        return (showtime.ShowtimeId, reservation.OrderId, seat.SeatId, paymentId, attemptKey, order.OrderCode, order.TotalVnd);
    }

    private static VerifiedMockPaymentMessage CreateVerifiedPayment(string attemptKey, string orderCode, long totalVnd)
    {
        var msg = new MockPaymentMessage(
            ProviderEventRef: "EVT-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "SUCCESS");
        return MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
    }

    [Fact]
    public async Task TC_WORKER_06a_PaymentCommittedBeforeDeadline_WorkerSweepPreservesPaidOrderAndSoldSeats()
    {
        // Scenario 1: Valid payment committed before deadline; worker running afterwards does not lose or corrupt PAID order or SOLD seats.
        var futureUtc = DateTime.UtcNow.AddMinutes(10);
        var orderData = await CreateOrderWithPaymentAttemptAsync(futureUtc);
        var verified = CreateVerifiedPayment(orderData.AttemptKey, orderData.OrderCode, orderData.TotalVnd);

        // 1. Payment committed successfully before deadline
        await using (var payDb = await fixture.CreateDbContextAsync())
        {
            var paySvc = new PaymentService(payDb, new SystemClock(), new QrTokenProtector());
            var payResult = await paySvc.ApplyVerifiedMockAsync(verified);
            Assert.Equal("APPLIED", payResult.Decision);
        }

        // 2. Advance / simulate time past original order expiry deadline (maintaining expires_utc > created_utc and resolved_utc <= expires_utc)
        var pastCreated = DateTime.UtcNow.AddMinutes(-30);
        var pastPaid = DateTime.UtcNow.AddMinutes(-20);
        var pastExpiry = DateTime.UtcNow.AddMinutes(-10);

        await using (var updateDb = await fixture.CreateDbContextAsync())
        {
            var ord = await updateDb.Orders.SingleAsync(o => o.OrderId == orderData.OrderId);
            ord.CreatedUtc = pastCreated;
            ord.PaidUtc = pastPaid;
            ord.ExpiresUtc = pastExpiry;

            var pay = await updateDb.Payments.SingleAsync(p => p.PaymentId == orderData.PaymentId);
            pay.ResolvedUtc = pastPaid;

            await updateDb.SaveChangesAsync();
        }

        // 3. Worker executes sweep after deadline has passed
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);

        // 4. Strict assertions: Order remains PAID, seats SOLD, tickets intact, payment SUCCESS
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var finalOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == orderData.OrderId);
        var finalSeat = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == orderData.ShowtimeId && s.SeatId == orderData.SeatId);
        var finalTickets = await verifyDb.Tickets.Where(t => t.OrderId == orderData.OrderId).ToListAsync();
        var finalPayment = await verifyDb.Payments.SingleAsync(p => p.PaymentId == orderData.PaymentId);

        Assert.Equal(OrderStates.Paid, finalOrder.Status);
        Assert.Equal(SeatStates.Sold, finalSeat.State);
        Assert.Null(finalSeat.HoldExpiresUtc);
        Assert.Single(finalTickets);
        Assert.Equal("ISSUED", finalTickets[0].Status);
        Assert.Equal("SUCCESS", finalPayment.Status);
    }

    [Fact]
    public async Task TC_WORKER_06b_ExpiryCommittedBeforeLatePayment_PreservesExpiredStatusAndMarksNeedsReview()
    {
        // Scenario 2: Expiry committed before late payment callback SUCCESS arrives; no tickets issued, payment processed as NEEDS_REVIEW.
        var pastUtc = DateTime.UtcNow.AddMinutes(-5);
        var orderData = await CreateOrderWithPaymentAttemptAsync(pastUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        // 1. Worker sweeps first, expiring the overdue order and releasing seat holds
        var swept = await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);
        Assert.True(swept >= 1);

        // Verify order is now EXPIRED and seat is AVAILABLE
        await using (var checkDb = await fixture.CreateDbContextAsync())
        {
            var ord = await checkDb.Orders.SingleAsync(o => o.OrderId == orderData.OrderId);
            Assert.Equal(OrderStates.Expired, ord.Status);
            var seat = await checkDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == orderData.ShowtimeId && s.SeatId == orderData.SeatId);
            Assert.Equal(SeatStates.Available, seat.State);
            Assert.Null(seat.OwnerOrderId);
        }

        // 2. Late payment callback arrives with SUCCESS outcome
        var verified = CreateVerifiedPayment(orderData.AttemptKey, orderData.OrderCode, orderData.TotalVnd);
        PaymentResult payResult;
        await using (var payDb = await fixture.CreateDbContextAsync())
        {
            var paySvc = new PaymentService(payDb, new SystemClock(), new QrTokenProtector());
            payResult = await paySvc.ApplyVerifiedMockAsync(verified);
        }

        // 3. Strict assertions: Decision is NEEDS_REVIEW, order remains EXPIRED (terminal history), zero tickets issued, seat remains AVAILABLE
        Assert.Equal("NEEDS_REVIEW", payResult.Decision);

        await using var verifyDb = await fixture.CreateDbContextAsync();
        var finalOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == orderData.OrderId);
        var finalSeat = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == orderData.ShowtimeId && s.SeatId == orderData.SeatId);
        var finalTickets = await verifyDb.Tickets.Where(t => t.OrderId == orderData.OrderId).ToListAsync();
        var finalPayment = await verifyDb.Payments.SingleAsync(p => p.PaymentId == orderData.PaymentId);

        Assert.Equal(OrderStates.Expired, finalOrder.Status);
        Assert.Equal(SeatStates.Available, finalSeat.State);
        Assert.Null(finalSeat.OwnerOrderId);
        Assert.Null(finalSeat.HoldExpiresUtc);
        Assert.Empty(finalTickets);
        Assert.Equal("NEEDS_REVIEW", finalPayment.Status);
    }

    [Fact]
    public async Task TC_WORKER_06c_ConcurrentPaymentAndExpiry_AtDeadlineBoundary_MaintainsStrictConsistency()
    {
        // Scenario 3: Live race at the deadline boundary contending for SqlServerLocks.ShowtimeAsync; strict invariant (atomic winner, zero hybrid states).
        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");

        for (int iteration = 0; iteration < 5; iteration++)
        {
            // Set deadline right at the boundary (expiring at current instant)
            var deadlineUtc = DateTime.UtcNow.AddMilliseconds(-10);
            var orderData = await CreateOrderWithPaymentAttemptAsync(deadlineUtc);
            var verified = CreateVerifiedPayment(orderData.AttemptKey, orderData.OrderCode, orderData.TotalVnd);

            var worker = new ReservationExpiryWorker(scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

            // Concurrently run Expiry Worker and Payment callback across separate DbContext instances
            var expiryTask = Task.Run(async () =>
            {
                return await worker.ProcessExpiredOrdersAsync(10, CancellationToken.None);
            });

            var paymentTask = Task.Run(async () =>
            {
                await using var payDb = await fixture.CreateDbContextAsync();
                var paySvc = new PaymentService(payDb, new SystemClock(), new QrTokenProtector());
                return await paySvc.ApplyVerifiedMockAsync(verified);
            });

            await Task.WhenAll(expiryTask, paymentTask);
            var payResult = await paymentTask;

            // Invariant Verification: Exact atomic outcome with ZERO hybrid or corrupt states
            await using var verifyDb = await fixture.CreateDbContextAsync();
            var finalOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == orderData.OrderId);
            var finalSeat = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == orderData.ShowtimeId && s.SeatId == orderData.SeatId);
            var finalTickets = await verifyDb.Tickets.Where(t => t.OrderId == orderData.OrderId).ToListAsync();
            var finalPayment = await verifyDb.Payments.SingleAsync(p => p.PaymentId == orderData.PaymentId);

            if (finalOrder.Status == OrderStates.Paid)
            {
                // Winner: Payment Service acquired lock first
                Assert.Equal(SeatStates.Sold, finalSeat.State);
                Assert.Null(finalSeat.HoldExpiresUtc);
                Assert.Single(finalTickets);
                Assert.Equal("ISSUED", finalTickets[0].Status);
                Assert.Equal("SUCCESS", finalPayment.Status);
                Assert.Equal("APPLIED", payResult.Decision);
            }
            else if (finalOrder.Status is OrderStates.Expired or OrderStates.Review)
            {
                // Winner: Expiry Worker acquired lock first
                Assert.Equal(SeatStates.Available, finalSeat.State);
                Assert.Null(finalSeat.OwnerOrderId);
                Assert.Null(finalSeat.HoldExpiresUtc);
                Assert.Empty(finalTickets);
                Assert.Contains(finalPayment.Status, new[] { "CREATED", "NEEDS_REVIEW" });
                Assert.Contains(payResult.Decision, new[] { "NEEDS_REVIEW", "REJECTED" });
            }
            else
            {
                Assert.Fail($"Unexpected order status after concurrency race: {finalOrder.Status}");
            }
        }
    }

    [Fact]
    public async Task TC_WORKER_07_RealHostedServiceExecution_SweepsExpiredReservations()
    {
        // Create an overdue reservation
        var pastUtc = DateTime.UtcNow.AddMinutes(-5);
        var (showtimeId, orderId, seatIds) = await CreateOrderAsync(OrderStates.Pending, pastUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BGP_ENABLE_EXPIRY_WORKER"] = "true",
                ["BGP_EXPIRY_INTERVAL_SECONDS"] = "1",
                ["BGP_EXPIRY_BATCH_SIZE"] = "50"
            })
            .Build();

        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start worker in background
        await worker.StartAsync(cts.Token);

        // Wait up to 3 seconds for worker timer to tick and sweep
        var swept = false;
        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(500);
            await using var checkDb = await fixture.CreateDbContextAsync();
            var status = await checkDb.Orders.Where(o => o.OrderId == orderId).Select(o => o.Status).SingleOrDefaultAsync();
            if (status == OrderStates.Expired)
            {
                swept = true;
                break;
            }
        }

        // Stop worker gracefully
        await worker.StopAsync(CancellationToken.None);

        Assert.True(swept, "Worker timer tick should have swept the overdue order to EXPIRED within interval.");
        Assert.True(worker.ExecuteTask == null || worker.ExecuteTask.IsCompletedSuccessfully,
            "Worker background execution task should complete successfully without unhandled exceptions.");

        // Verify seats were released
        await using var db = await fixture.CreateDbContextAsync();
        var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId && seatIds.Contains(s.SeatId)).ToListAsync();
        Assert.All(seats, s => Assert.Equal(SeatStates.Available, s.State));
    }
}
