using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed record ReservationResult(long OrderId, string OrderCode, DateTime ExpiresUtc, long TotalVnd);

public sealed class SeatReservationService(BetaDbContext db, ISystemClock clock)
{
    public async Task<ReservationResult> ReserveAsync(long customerId, long showtimeId,
        IReadOnlyCollection<long> seatIds, CancellationToken ct = default)
    {
        if (seatIds.Count is < 1 or > 8 || seatIds.Any(id => id <= 0) || seatIds.Distinct().Count() != seatIds.Count)
            throw new BusinessRuleException("INVALID_SELECTION", "Select 1–8 distinct valid seat IDs.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await SqlServerLocks.ShowtimeAsync(db, showtimeId, ct))
            throw new BusinessRuleException("SHOWTIME_NOT_FOUND", "Unknown showtime.");
        await SecurityRules.EnsureRoleAsync(db, customerId, ct, "CUSTOMER");
        var now = clock.UtcNow;
        var showtime = await db.Showtimes.SingleAsync(s => s.ShowtimeId == showtimeId, ct);
        if (showtime.Status != "ON_SALE" || showtime.StartUtc <= now)
            throw new BusinessRuleException("NOT_ON_SALE", "Showtime is not open for booking.");

        var selected = await (from ss in db.ShowtimeSeats
            join seat in db.Seats on ss.SeatId equals seat.SeatId
            where ss.ShowtimeId == showtimeId && seatIds.Contains(ss.SeatId)
            select new { Stock = ss, Seat = seat }).ToListAsync(ct);
        if (selected.Count != seatIds.Count || selected.Any(s => s.Seat.RoomId != showtime.RoomId ||
            s.Stock.RoomId != showtime.RoomId || !s.Seat.IsActive ||
            !(s.Stock.State == SeatStates.Available ||
              (s.Stock.State == SeatStates.Held && s.Stock.HoldExpiresUtc <= now))))
            throw new BusinessRuleException("SEAT_NOT_AVAILABLE", "One or more selected seats are not available.");

        var seatTypes = selected.Select(s => s.Seat.SeatTypeCode).Distinct().ToArray();
        var prices = await db.SeatPrices.Where(p => p.ShowtimeId == showtimeId &&
            seatTypes.Contains(p.SeatTypeCode)).ToDictionaryAsync(p => p.SeatTypeCode, p => p.AmountVnd, ct);
        if (selected.Any(s => !prices.ContainsKey(s.Seat.SeatTypeCode)))
            throw new BusinessRuleException("PRICE_NOT_CONFIGURED", "Price is missing for a selected seat type.");
        var total = selected.Aggregate(0L, (sum, s) => checked(sum + prices[s.Seat.SeatTypeCode]));
        var expires = now.AddMinutes(5); // BGP prototype policy; not an official Beta rule.
        var order = new BookingOrder
        {
            OrderCode = "BGP-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            UserId = customerId, ShowtimeId = showtimeId, Status = OrderStates.Pending,
            SeatCount = checked((byte)seatIds.Count), SubtotalVnd = total,
            FeeVnd = 0, DiscountVnd = 0, TotalVnd = total,
            CreatedUtc = now, ExpiresUtc = expires
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct); // Still INSIDE the transaction, to allocate order_id.
        foreach (var s in selected)
        {
            s.Stock.State = SeatStates.Held;
            s.Stock.OwnerOrderId = order.OrderId;
            s.Stock.HoldExpiresUtc = expires;
            db.OrderSeats.Add(new OrderSeat
            {
                OrderId = order.OrderId, ShowtimeId = showtimeId, SeatId = s.Seat.SeatId,
                SeatTypeSnapshot = s.Seat.SeatTypeCode,
                UnitPriceVnd = prices[s.Seat.SeatTypeCode]
            });
        }
        db.AuditLogs.Add(Auditing.Event(customerId, "ORDER_HELD", "Order", order.OrderId.ToString(),
            new { showtimeId, seatIds, expires, total }, now));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException("SEAT_CONFLICT", "Seat changed concurrently; retry the selection.");
        }
        await tx.CommitAsync(ct);
        return new ReservationResult(order.OrderId, order.OrderCode, expires, total);
    }
}
