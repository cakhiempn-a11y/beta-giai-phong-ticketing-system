using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class QrScanningTests(TestDatabaseFixture fixture)
{
    private async Task<(long ShowtimeId, string ValidToken, long StaffId, DateTime ShowtimeStartUtc)> PrepareIssuedTicketAsync()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.FirstAsync();
        var room = await db.Rooms.FirstAsync();
        var manager = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var staff = await db.Users.FirstAsync(u => u.NormalizedEmail == "STAFF@DEMO.INVALID");
        var customer = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        var startUtc = DateTime.UtcNow.AddDays(1);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(120),
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

        var reservation = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customer.UserId, showtime.ShowtimeId, [seat.SeatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customer.UserId, reservation.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-QR-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        await paymentService.ApplyVerifiedMockAsync(verified);

        var ticket = await db.Tickets.SingleAsync(t => t.OrderId == reservation.OrderId);
        var token = new QrTokenProtector().Unseal(ticket.QrTokenCipher);

        return (showtime.ShowtimeId, token, staff.UserId, startUtc);
    }

    [Fact]
    public async Task TC_QR_01_ValidScan_AcceptsTicketAndMarksUsed()
    {
        var (showtimeId, token, staffId, startUtc) = await PrepareIssuedTicketAsync();
        await using var db = await fixture.CreateDbContextAsync();

        // 10 minutes before startUtc (within allowed window)
        var scanClock = new FixedClock(startUtc.AddMinutes(-10));
        var result = await new TicketScanningService(db, scanClock)
            .ScanAsync(staffId, token, showtimeId);

        Assert.Equal("ACCEPTED", result.Outcome);

        var ticket = await db.Tickets.SingleAsync(t => t.TicketId == result.TicketId);
        Assert.Equal("USED", ticket.Status);
        Assert.NotNull(ticket.UsedUtc);
    }

    [Fact]
    public async Task TC_QR_02_ScanAlreadyUsedTicket_ReturnsAlreadyUsed()
    {
        var (showtimeId, token, staffId, startUtc) = await PrepareIssuedTicketAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var scanClock = new FixedClock(startUtc.AddMinutes(-10));
        var scanningService = new TicketScanningService(db, scanClock);

        var res1 = await scanningService.ScanAsync(staffId, token, showtimeId);
        Assert.Equal("ACCEPTED", res1.Outcome);

        var res2 = await scanningService.ScanAsync(staffId, token, showtimeId);
        Assert.Equal("ALREADY_USED", res2.Outcome);
    }

    [Fact]
    public async Task TC_QR_03_ConcurrentScanOfSameTicket_ExactlyOneAccepted()
    {
        var (showtimeId, token, staffId, startUtc) = await PrepareIssuedTicketAsync();
        var scanClock = new FixedClock(startUtc.AddMinutes(-10));

        var task1 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            return (await new TicketScanningService(localDb, scanClock).ScanAsync(staffId, token, showtimeId)).Outcome;
        });

        var task2 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            return (await new TicketScanningService(localDb, scanClock).ScanAsync(staffId, token, showtimeId)).Outcome;
        });

        var results = await Task.WhenAll(task1, task2);
        Assert.Single(results, "ACCEPTED");
        Assert.Single(results, "ALREADY_USED");
    }

    [Fact]
    public async Task TC_QR_04_ForgedToken_ReturnsNotFound()
    {
        var (showtimeId, _, staffId, startUtc) = await PrepareIssuedTicketAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var scanClock = new FixedClock(startUtc.AddMinutes(-10));
        var result = await new TicketScanningService(db, scanClock)
            .ScanAsync(staffId, "FORGED_NON_EXISTENT_QR_TOKEN_STRING_12345", showtimeId);

        Assert.Equal("NOT_FOUND", result.Outcome);

        // Verify audit log
        var audit = await db.AuditLogs.AnyAsync(a => a.EventType == "TICKET_SCAN_REJECTED");
        Assert.True(audit);
    }

    [Fact]
    public async Task TC_QR_05_WrongShowtime_ReturnsWrongShowtime()
    {
        var (showtimeId, token, staffId, startUtc) = await PrepareIssuedTicketAsync();
        await using var db = await fixture.CreateDbContextAsync();

        var scanClock = new FixedClock(startUtc.AddMinutes(-10));
        var result = await new TicketScanningService(db, scanClock)
            .ScanAsync(staffId, token, showtimeId + 999L);

        Assert.Equal("WRONG_SHOWTIME", result.Outcome);

        var ticket = await db.Tickets.SingleAsync(t => t.TicketId == result.TicketId);
        Assert.Equal("ISSUED", ticket.Status); // Not consumed
    }

    [Fact]
    public async Task TC_QR_06_OutsideWindow_ReturnsOutsideWindow()
    {
        var (showtimeId, token, staffId, startUtc) = await PrepareIssuedTicketAsync();
        await using var db = await fixture.CreateDbContextAsync();

        // 60 minutes before start (allowed window starts -30m)
        var earlyClock = new FixedClock(startUtc.AddMinutes(-60));
        var result = await new TicketScanningService(db, earlyClock)
            .ScanAsync(staffId, token, showtimeId);

        Assert.Equal("OUTSIDE_WINDOW", result.Outcome);

        var ticket = await db.Tickets.SingleAsync(t => t.TicketId == result.TicketId);
        Assert.Equal("ISSUED", ticket.Status); // Not consumed
    }
}
