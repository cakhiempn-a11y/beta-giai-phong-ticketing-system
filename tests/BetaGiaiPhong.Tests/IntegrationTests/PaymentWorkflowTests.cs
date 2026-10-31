using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class PaymentWorkflowTests(TestDatabaseFixture fixture)
{
    private async Task<(long ShowtimeId, long SeatId, long CustomerId)> PrepareScenarioAsync()
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
            StartUtc = DateTime.UtcNow.AddDays(2),
            EndUtc = DateTime.UtcNow.AddDays(2).AddMinutes(120),
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
        return (showtime.ShowtimeId, seat.SeatId, customer.UserId);
    }

    [Fact]
    public async Task TC_PAY_01_ValidPayment_IssuesTicketAndMarksOrderPaid()
    {
        var (showtimeId, seatId, customerId) = await PrepareScenarioAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customerId, showtimeId, [seatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        var paymentId = await paymentService.CreateMockAttemptAsync(customerId, reservation.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        var result = await paymentService.ApplyVerifiedMockAsync(verified);

        Assert.Equal("APPLIED", result.Decision);
        Assert.Equal("PAID", result.OrderStatus);

        var order = await db.Orders.SingleAsync(o => o.OrderId == reservation.OrderId);
        Assert.Equal("PAID", order.Status);
        Assert.NotNull(order.PaidUtc);

        var ticket = await db.Tickets.SingleAsync(t => t.OrderId == reservation.OrderId);
        Assert.Equal("ISSUED", ticket.Status);

        var seat = await db.ShowtimeSeats.SingleAsync(ss => ss.ShowtimeId == showtimeId && ss.SeatId == seatId);
        Assert.Equal("SOLD", seat.State);
    }

    [Fact]
    public async Task TC_PAY_02_PaymentFailure_ReleasesSeatAndMarksOrderFailed()
    {
        var (showtimeId, seatId, customerId) = await PrepareScenarioAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customerId, showtimeId, [seatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customerId, reservation.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-FAIL-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd, "FAILED");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        var result = await paymentService.ApplyVerifiedMockAsync(verified);

        Assert.Equal("FAILED_RECORDED", result.Decision);
        Assert.Equal("FAILED", result.OrderStatus);

        var order = await db.Orders.SingleAsync(o => o.OrderId == reservation.OrderId);
        Assert.Equal("FAILED", order.Status);

        var seat = await db.ShowtimeSeats.SingleAsync(ss => ss.ShowtimeId == showtimeId && ss.SeatId == seatId);
        Assert.Equal("AVAILABLE", seat.State);
        Assert.Null(seat.OwnerOrderId);

        Assert.False(await db.Tickets.AnyAsync(t => t.OrderId == reservation.OrderId));
    }

    [Fact]
    public async Task TC_PAY_03_DuplicateWebhookCallbacks_AreIdempotent()
    {
        var (showtimeId, seatId, customerId) = await PrepareScenarioAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customerId, showtimeId, [seatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customerId, reservation.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-DUP-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));

        var res1 = await paymentService.ApplyVerifiedMockAsync(verified);
        var res2 = await paymentService.ApplyVerifiedMockAsync(verified);
        var res3 = await paymentService.ApplyVerifiedMockAsync(verified);

        Assert.Equal("APPLIED", res1.Decision);
        Assert.Equal("DUPLICATE", res2.Decision);
        Assert.Equal("DUPLICATE", res3.Decision);

        var tickets = await db.Tickets.Where(t => t.OrderId == reservation.OrderId).ToListAsync();
        Assert.Single(tickets);
    }

    [Fact]
    public async Task TC_PAY_04_LatePayment_DoesNotReviveExpiredOrder_MarksNeedsReview()
    {
        var (showtimeId, seatId, customerId) = await PrepareScenarioAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customerId, showtimeId, [seatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customerId, reservation.OrderId, attemptKey);

        // Fast forward clock past expiry and expire the order
        var lateClock = new FixedClock(reservation.ExpiresUtc.AddSeconds(1));
        var expired = await new OrderLifecycleService(db, lateClock).ExpireAsync(reservation.OrderId);
        Assert.True(expired);

        // Now payment callback arrives late
        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-LATE-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        var result = await new PaymentService(db, lateClock, new QrTokenProtector()).ApplyVerifiedMockAsync(verified);

        Assert.Equal("NEEDS_REVIEW", result.Decision);
        Assert.Equal("EXPIRED", result.OrderStatus);

        var order = await db.Orders.SingleAsync(o => o.OrderId == reservation.OrderId);
        Assert.Equal("EXPIRED", order.Status);

        var payment = await db.Payments.SingleAsync(p => p.OrderId == reservation.OrderId);
        Assert.Equal("NEEDS_REVIEW", payment.Status);

        Assert.False(await db.Tickets.AnyAsync(t => t.OrderId == reservation.OrderId));
    }

    [Fact]
    public async Task TC_PAY_05_InvalidHmacSignature_ThrowsUnauthorizedException()
    {
        var msg = new MockPaymentMessage("EVT-1", "ATT-1", "TXN-1", "ORD-1", 70000, "SUCCESS");
        Assert.Throws<UnauthorizedAccessException>(() => MockPaymentSecurity.Verify(msg, "INVALID_HEX_SIGNATURE_0123456789"));
    }

    [Fact]
    public async Task TC_PAY_06_AmountMismatch_RejectsPayment()
    {
        var (showtimeId, seatId, customerId) = await PrepareScenarioAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customerId, showtimeId, [seatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customerId, reservation.OrderId, attemptKey);

        // Send callback with wrong amount
        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-AMT-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd + 10000, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        var result = await paymentService.ApplyVerifiedMockAsync(verified);

        Assert.Equal("REJECTED", result.Decision);
        var order = await db.Orders.SingleAsync(o => o.OrderId == reservation.OrderId);
        Assert.Equal("PENDING_PAYMENT", order.Status);
    }
}
