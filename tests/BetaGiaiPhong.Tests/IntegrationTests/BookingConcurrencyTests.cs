using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.IntegrationTests;

[Collection("DatabaseCollection")]
public sealed class BookingConcurrencyTests(TestDatabaseFixture fixture)
{
    private async Task<long> CreateIsolatedShowtimeAsync(int seatCount = 10)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.FirstAsync();
        var room = await db.Rooms.FirstAsync();
        var manager = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");

        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = DateTime.UtcNow.AddDays(2),
            EndUtc = DateTime.UtcNow.AddDays(2).AddMinutes(120),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = seatCount,
            CreatedBy = manager.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seats = await db.Seats.Where(s => s.RoomId == room.RoomId && s.IsActive).Take(seatCount).ToListAsync();
        foreach (var seat in seats)
        {
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = seat.SeatId,
                RoomId = room.RoomId,
                State = "AVAILABLE"
            });
        }
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
        return showtime.ShowtimeId;
    }

    [Fact]
    public async Task TC_CON_01a_TwoConcurrentRequestsForSameSeat_ExactlyOneSucceeds()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.FirstAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE");
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var customer2 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID");

        var task1 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            try
            {
                await new SeatReservationService(localDb, new SystemClock())
                    .ReserveAsync(customer1.UserId, showtimeId, [seat.SeatId]);
                return "SUCCESS";
            }
            catch (BusinessRuleException ex) when (ex.Code is "SEAT_NOT_AVAILABLE" or "SEAT_CONFLICT")
            {
                return "CONFLICT";
            }
        });

        var task2 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            try
            {
                await new SeatReservationService(localDb, new SystemClock())
                    .ReserveAsync(customer2.UserId, showtimeId, [seat.SeatId]);
                return "SUCCESS";
            }
            catch (BusinessRuleException ex) when (ex.Code is "SEAT_NOT_AVAILABLE" or "SEAT_CONFLICT")
            {
                return "CONFLICT";
            }
        });

        var results = await Task.WhenAll(task1, task2);
        Assert.Single(results, "SUCCESS");
        Assert.Single(results, "CONFLICT");
    }

    [Fact]
    public async Task TC_CON_01b_TenConcurrentRequestsForSameSeat_ExactlyOneSucceeds()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.FirstAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE");
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            try
            {
                await new SeatReservationService(localDb, new SystemClock())
                    .ReserveAsync(customer1.UserId, showtimeId, [seat.SeatId]);
                return "SUCCESS";
            }
            catch (BusinessRuleException ex) when (ex.Code is "SEAT_NOT_AVAILABLE" or "SEAT_CONFLICT")
            {
                return "CONFLICT";
            }
        })).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Single(results, "SUCCESS");
        Assert.Equal(9, results.Count(r => r == "CONFLICT"));
    }

    [Fact]
    public async Task TC_CON_01c_FiftyConcurrentRequestsForSameSeat_ExactlyOneSucceeds()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.FirstAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE");
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            try
            {
                await new SeatReservationService(localDb, new SystemClock())
                    .ReserveAsync(customer1.UserId, showtimeId, [seat.SeatId]);
                return "SUCCESS";
            }
            catch (BusinessRuleException ex) when (ex.Code is "SEAT_NOT_AVAILABLE" or "SEAT_CONFLICT")
            {
                return "CONFLICT";
            }
        })).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Single(results, "SUCCESS");
        Assert.Equal(49, results.Count(r => r == "CONFLICT"));
    }

    [Fact]
    public async Task TC_CON_02_TwoConcurrentRequestsForDifferentSeats_BothSucceed()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seats = await db.ShowtimeSeats.Where(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE").Take(2).ToListAsync();
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var customer2 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID");

        var task1 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            return await new SeatReservationService(localDb, new SystemClock())
                .ReserveAsync(customer1.UserId, showtimeId, [seats[0].SeatId]);
        });

        var task2 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            return await new SeatReservationService(localDb, new SystemClock())
                .ReserveAsync(customer2.UserId, showtimeId, [seats[1].SeatId]);
        });

        var res = await Task.WhenAll(task1, task2);
        Assert.NotEqual(res[0].OrderId, res[1].OrderId);
        Assert.True(res[0].TotalVnd > 0);
        Assert.True(res[1].TotalVnd > 0);
    }

    [Fact]
    public async Task TC_CON_03_OverlappingSeatRequests_MaintainsAllOrNothingAtomicity()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seats = await db.ShowtimeSeats.Where(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE").Take(3).ToListAsync();
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var customer2 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID");

        // Request 1: [Seat0, Seat1]
        // Request 2: [Seat1, Seat2]
        var task1 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            try
            {
                await new SeatReservationService(localDb, new SystemClock())
                    .ReserveAsync(customer1.UserId, showtimeId, [seats[0].SeatId, seats[1].SeatId]);
                return "SUCCESS";
            }
            catch (BusinessRuleException)
            {
                return "CONFLICT";
            }
        });

        var task2 = Task.Run(async () =>
        {
            await using var localDb = await fixture.CreateDbContextAsync();
            try
            {
                await new SeatReservationService(localDb, new SystemClock())
                    .ReserveAsync(customer2.UserId, showtimeId, [seats[1].SeatId, seats[2].SeatId]);
                return "SUCCESS";
            }
            catch (BusinessRuleException)
            {
                return "CONFLICT";
            }
        });

        var results = await Task.WhenAll(task1, task2);
        Assert.Single(results, "SUCCESS");
        Assert.Single(results, "CONFLICT");

        // Verify database: exactly 2 seats are HELD, 1 remains AVAILABLE. No partial orphan holds!
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var heldSeats = await verifyDb.ShowtimeSeats.CountAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "HELD");
        var availableSeats = await verifyDb.ShowtimeSeats.CountAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE");
        Assert.Equal(2, heldSeats);
        Assert.Equal(8, availableSeats);
    }

    [Fact]
    public async Task TC_CON_04_ExpiredSeatReservation_ReclaimedByNewCustomer()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.FirstAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE");
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var customer2 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID");

        var res1 = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customer1.UserId, showtimeId, [seat.SeatId]);

        // Fast forward clock past ExpiresUtc
        var futureClock = new FixedClock(res1.ExpiresUtc.AddSeconds(1));
        await new OrderLifecycleService(db, futureClock).ExpireAsync(res1.OrderId);

        // Customer 2 reclaims the expired seat
        var res2 = await new SeatReservationService(db, futureClock)
            .ReserveAsync(customer2.UserId, showtimeId, [seat.SeatId]);

        Assert.NotEqual(res1.OrderId, res2.OrderId);
        var updatedSeat = await db.ShowtimeSeats.SingleAsync(ss => ss.ShowtimeId == showtimeId && ss.SeatId == seat.SeatId);
        Assert.Equal(res2.OrderId, updatedSeat.OwnerOrderId);
        Assert.Equal("HELD", updatedSeat.State);
    }

    [Fact]
    public async Task TC_CON_05_InvalidSeatInRequest_RollsBackEntireTransaction()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.FirstAsync(ss => ss.ShowtimeId == showtimeId && ss.State == "AVAILABLE");
        var customer1 = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new SeatReservationService(db, new SystemClock())
                .ReserveAsync(customer1.UserId, showtimeId, [seat.SeatId, 99999999L]));

        // Verify clean rollback: seat remains AVAILABLE, owner is null
        var verifySeat = await db.ShowtimeSeats.SingleAsync(ss => ss.ShowtimeId == showtimeId && ss.SeatId == seat.SeatId);
        Assert.Equal("AVAILABLE", verifySeat.State);
        Assert.Null(verifySeat.OwnerOrderId);
    }
}

internal sealed class FixedClock(DateTime instant) : ISystemClock
{
    public DateTime UtcNow => instant;
}
