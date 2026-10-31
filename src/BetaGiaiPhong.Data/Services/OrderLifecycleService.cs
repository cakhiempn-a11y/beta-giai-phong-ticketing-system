using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed class OrderLifecycleService(BetaDbContext db, ISystemClock clock)
{
    public async Task<bool> CancelAsync(long customerId, long orderId, CancellationToken ct = default)
        => await FinishPendingAsync(orderId, OrderStates.Cancelled, customerId, false, ct);

    public async Task<bool> ExpireAsync(long orderId, CancellationToken ct = default)
        => await FinishPendingAsync(orderId, OrderStates.Expired, null, true, ct);

    public async Task<int> ExpireDueAsync(int limit = 100, CancellationToken ct = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        var now = clock.UtcNow;
        var candidates = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStates.Pending && o.ExpiresUtc <= now)
            .OrderBy(o => o.ExpiresUtc).Take(limit).Select(o => o.OrderId).ToListAsync(ct);
        int finished = 0;
        foreach (var orderId in candidates)
        {
            // A short-lived context per item is preferable for a high-volume hosted worker;
            // demo runner calls this method with a fresh DbContext.
            if (await ExpireAsync(orderId, ct)) finished++;
        }
        return finished;
    }

    private async Task<bool> FinishPendingAsync(long orderId, string target,
        long? actorId, bool requireExpired, CancellationToken ct)
    {
        var pre = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (pre is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await SqlServerLocks.ShowtimeAsync(db, pre.ShowtimeId, ct);
        if (actorId is long actor) await SecurityRules.EnsureRoleAsync(db, actor, ct, "CUSTOMER");
        var order = await db.Orders.SingleAsync(o => o.OrderId == orderId, ct);
        if (actorId is long uid && order.UserId != uid)
            throw new BusinessRuleException("FORBIDDEN", "Customer cannot cancel someone else's order.");
        if (order.Status != OrderStates.Pending || (requireExpired && order.ExpiresUtc > clock.UtcNow))
        {
            await tx.CommitAsync(ct);
            return false;
        }
        // The payment callback also locks the showtime; no PAID -> cancelled race.
        order.Status = target;
        var holds = await db.ShowtimeSeats.Where(s => s.ShowtimeId == order.ShowtimeId &&
            s.OwnerOrderId == orderId && s.State == SeatStates.Held).ToListAsync(ct);
        foreach (var seat in holds)
        {
            seat.State = SeatStates.Available;
            seat.OwnerOrderId = null;
            seat.HoldExpiresUtc = null;
        }
        db.AuditLogs.Add(Auditing.Event(actorId, "ORDER_" + target, "Order", orderId.ToString(),
            new { target }, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
