using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class ReportingAndIntegrityTests(TestDatabaseFixture fixture)
{
    [Fact]
    public async Task TC_INT_01_MultiTableIntegrityChecks_ZeroViolations()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var checks = new (string Name, string Sql)[]
        {
            ("Wrong room/seat", "SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss JOIN dbo.Seats s ON s.seat_id=ss.seat_id JOIN dbo.Showtimes st ON st.showtime_id=ss.showtime_id WHERE s.room_id<>st.room_id OR ss.room_id<>st.room_id"),
            ("SOLD seat without PAID order", "SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss LEFT JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='SOLD' AND (o.order_id IS NULL OR o.status<>'PAID')"),
            ("PAID orders missing tickets", "SELECT COUNT_BIG(*) FROM dbo.Orders o OUTER APPLY (SELECT COUNT_BIG(*) n FROM dbo.Tickets t WHERE t.order_id=o.order_id) x WHERE o.status='PAID' AND x.n<>o.seat_count"),
            ("Order price snapshot mismatch", "SELECT COUNT_BIG(*) FROM dbo.Orders o OUTER APPLY (SELECT COALESCE(SUM(os.unit_price_vnd),0) v FROM dbo.OrderSeats os WHERE os.order_id=o.order_id) x WHERE x.v<>o.subtotal_vnd"),
            ("Late payment improperly SUCCESS", "SELECT COUNT_BIG(*) FROM dbo.Payments p JOIN dbo.Orders o ON o.order_id=p.order_id WHERE p.status='SUCCESS' AND o.status IN ('EXPIRED','CANCELLED')")
        };

        long totalViolations = 0;
        await db.Database.OpenConnectionAsync();
        foreach (var (name, sql) in checks)
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            var violations = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.True(violations == 0, $"Integrity check '{name}' failed with {violations} violation(s).");
            totalViolations += violations;
        }

        Assert.Equal(0, totalViolations);
    }

    [Fact]
    public async Task TC_REP_01_ShowtimeSalesView_CalculatesCorrectMetricsViaLinq()
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
            StartUtc = DateTime.UtcNow.AddDays(3),
            EndUtc = DateTime.UtcNow.AddDays(3).AddMinutes(120),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = manager.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seat = await db.Seats.FirstAsync(s => s.RoomId == room.RoomId && s.IsActive);
        db.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = "AVAILABLE"
        });
        foreach (var st in await db.SeatTypes.ToListAsync())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st.SeatTypeCode,
                AmountVnd = 70000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        // Check report view with 0 sales
        var summaryBefore = await db.ShowtimeSales.SingleAsync(s => s.ShowtimeId == showtime.ShowtimeId);
        Assert.Equal(0, summaryBefore.TicketsSold);
        Assert.Equal(0m, summaryBefore.RevenueVnd);
        Assert.Equal(10, summaryBefore.SellableCapacity);
        Assert.Equal(0m, summaryBefore.OccupancyPct);

        // Make 1 sale
        var res = await new SeatReservationService(db, new SystemClock()).ReserveAsync(customer.UserId, showtime.ShowtimeId, [seat.SeatId]);
        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customer.UserId, res.OrderId, attemptKey);
        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-REP-" + Guid.NewGuid().ToString("N"), res.OrderCode, res.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        await paymentService.ApplyVerifiedMockAsync(verified);

        // Check report view with 1 sale
        var summaryAfter = await db.ShowtimeSales.AsNoTracking().SingleAsync(s => s.ShowtimeId == showtime.ShowtimeId);
        Assert.Equal(1, summaryAfter.TicketsSold);
        Assert.Equal(70000m, summaryAfter.RevenueVnd);
        Assert.Equal(10, summaryAfter.SellableCapacity);
        Assert.Equal(10.00m, summaryAfter.OccupancyPct);
    }
}
