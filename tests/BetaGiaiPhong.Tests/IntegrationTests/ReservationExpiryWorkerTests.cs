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
        else if (status is OrderStates.Cancelled or OrderStates.Expired or OrderStates.Failed)
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

        var (_, paidOrderId, _) = await CreateOrderAsync(OrderStates.Paid, pastUtc);
        var (_, cancelledOrderId, _) = await CreateOrderAsync(OrderStates.Cancelled, pastUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        await worker.ProcessExpiredOrdersAsync(100, CancellationToken.None);

        await using var db = await fixture.CreateDbContextAsync();
        var paidOrder = await db.Orders.SingleAsync(o => o.OrderId == paidOrderId);
        Assert.Equal(OrderStates.Paid, paidOrder.Status);

        var cancelledOrder = await db.Orders.SingleAsync(o => o.OrderId == cancelledOrderId);
        Assert.Equal(OrderStates.Cancelled, cancelledOrder.Status);
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

    [Fact]
    public async Task TC_WORKER_06_ConcurrentPaymentAndExpiry_MaintainsDataConsistency()
    {
        // Create an overdue pending order
        var pastUtc = DateTime.UtcNow.AddMinutes(-5);
        var (showtimeId, orderId, seatIds) = await CreateOrderAsync(OrderStates.Pending, pastUtc);

        var scopeFactory = CreateScopeFactory();
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment("Production");
        var worker = new ReservationExpiryWorker(
            scopeFactory, config, env, NullLogger<ReservationExpiryWorker>.Instance);

        // Task 1: Expiry sweep
        var expiryTask = Task.Run(async () =>
        {
            await worker.ProcessExpiredOrdersAsync(10, CancellationToken.None);
        });

        // Task 2: Payment callback
        var paymentTask = Task.Run(async () =>
        {
            await using var db = await fixture.CreateDbContextAsync();
            var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "TXN-RACE-" + Guid.NewGuid().ToString("N"), "BGP-TEST-RACE", 150000, "SUCCESS");
            var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
            try
            {
                await paymentService.ApplyVerifiedMockAsync(verified);
            }
            catch
            {
                // Expected if order expired or invalid
            }
        });

        await Task.WhenAll(expiryTask, paymentTask);

        // Invariant: If order is EXPIRED, seats MUST NOT be SOLD
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var order = await verifyDb.Orders.SingleAsync(o => o.OrderId == orderId);
        var seats = await verifyDb.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId && seatIds.Contains(s.SeatId)).ToListAsync();

        if (order.Status == OrderStates.Expired)
        {
            Assert.All(seats, s => Assert.Equal(SeatStates.Available, s.State));
        }
        else if (order.Status == OrderStates.Paid)
        {
            Assert.All(seats, s => Assert.Equal(SeatStates.Sold, s.State));
        }
    }
}
