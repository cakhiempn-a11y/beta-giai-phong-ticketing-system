using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed record SeatPriceInput(string SeatTypeCode, long AmountVnd);

public sealed class ShowtimePublishingService(BetaDbContext db, ISystemClock clock)
{
    public async Task<long> PublishAsync(
        long managerId, long roomId, long movieId, DateTime startUtc,
        short bufferMinutes, IReadOnlyCollection<SeatPriceInput> prices,
        CancellationToken ct = default)
    {
        if (startUtc.Kind != DateTimeKind.Utc || startUtc <= clock.UtcNow || bufferMinutes < 0)
            throw new BusinessRuleException("INVALID_TIME", "Start time must be future UTC; buffer cannot be negative.");
        if (prices.Count == 0 || prices.Any(p => p.AmountVnd <= 0) ||
            prices.Select(p => p.SeatTypeCode).Distinct().Count() != prices.Count)
            throw new BusinessRuleException("INVALID_PRICE", "Exactly one positive price per seat type is required.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await SqlServerLocks.RoomAsync(db, roomId, ct))
            throw new BusinessRuleException("ROOM_NOT_FOUND", "Unknown room.");
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");
        var room = await db.Rooms.SingleAsync(x => x.RoomId == roomId, ct);
        var movie = await db.Movies.SingleOrDefaultAsync(x => x.MovieId == movieId, ct);
        if (room.Status != "ACTIVE" || movie is null || movie.Status != "PUBLISHED")
            throw new BusinessRuleException("NOT_PUBLISHABLE", "An active room and a published movie are required.");
        var endUtc = startUtc.AddMinutes(movie.DurationMin);
        // Room lock makes this read-then-write overlap test serializable among cooperating writers.
        var schedule = await db.Showtimes
            .Where(s => s.RoomId == roomId && s.Status != "CANCELLED")
            .Select(s => new { s.StartUtc, s.EndUtc, s.BufferMinutes }).ToListAsync(ct);
        if (schedule.Any(s => startUtc < s.EndUtc.AddMinutes(s.BufferMinutes) &&
                              s.StartUtc < endUtc.AddMinutes(bufferMinutes)))
            throw new BusinessRuleException("SCHEDULE_OVERLAP", "Another showtime overlaps or violates the buffer.");

        var activeSeats = await (from seat in db.Seats
            join type in db.SeatTypes on seat.SeatTypeCode equals type.SeatTypeCode
            where seat.RoomId == roomId && seat.IsActive && type.IsActive
            select new { seat.SeatId, seat.SeatTypeCode }).ToListAsync(ct);
        if (activeSeats.Count == 0 ||
            activeSeats.Select(s => s.SeatTypeCode).Distinct().Except(prices.Select(p => p.SeatTypeCode)).Any())
            throw new BusinessRuleException("MISSING_PRICES", "Every sellable seat type requires a price.");

        var now = clock.UtcNow;
        var showtime = new Showtime
        {
            MovieId = movieId, RoomId = roomId, StartUtc = startUtc,
            EndUtc = endUtc, BufferMinutes = bufferMinutes, Status = "ON_SALE",
            SellableCapacitySnapshot = activeSeats.Count, CreatedBy = managerId, CreatedUtc = now
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync(ct);
        foreach (var seat in activeSeats)
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId, SeatId = seat.SeatId, RoomId = roomId,
                State = SeatStates.Available
            });
        foreach (var price in prices)
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = price.SeatTypeCode, AmountVnd = price.AmountVnd, UpdatedUtc = now
            });
        db.AuditLogs.Add(Auditing.Event(managerId, "SHOWTIME_PUBLISHED", "Showtime",
            showtime.ShowtimeId.ToString(), new { roomId, movieId, startUtc, activeSeats.Count }, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return showtime.ShowtimeId;
    }
}
