using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed record PaymentResult(string Decision, long OrderId, string OrderStatus);

public sealed class PaymentService(BetaDbContext db, ISystemClock clock, QrTokenProtector qr)
{
    public async Task<long> CreateMockAttemptAsync(long customerId, long orderId, string attemptKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(attemptKey) || attemptKey.Length > 80)
            throw new BusinessRuleException("INVALID_ATTEMPT_KEY", "Specify an attempt key (max 80 characters).");
        var pre = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OrderId == orderId, ct)
            ?? throw new BusinessRuleException("ORDER_NOT_FOUND", "Unknown order.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await SqlServerLocks.ShowtimeAsync(db, pre.ShowtimeId, ct);
        await SecurityRules.EnsureRoleAsync(db, customerId, ct, "CUSTOMER");
        var order = await db.Orders.SingleAsync(o => o.OrderId == orderId, ct);
        if (order.UserId != customerId || order.Status != OrderStates.Pending || order.ExpiresUtc <= clock.UtcNow)
            throw new BusinessRuleException("ORDER_UNAVAILABLE", "Order is not owned by the customer or has expired.");
        var prior = await db.Payments.SingleOrDefaultAsync(p => p.OrderId == orderId && p.AttemptKey == attemptKey, ct);
        if (prior is not null)
        {
            await tx.CommitAsync(ct);
            return prior.PaymentId;
        }
        var payment = new Payment
        {
            OrderId = orderId, Provider = "MOCK", AttemptKey = attemptKey,
            AmountVnd = order.TotalVnd, Status = "CREATED", CreatedUtc = clock.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return payment.PaymentId;
    }

    public async Task<PaymentResult> ApplyVerifiedMockAsync(VerifiedMockPaymentMessage verified,
        CancellationToken ct = default)
    {
        // VerifiedMockPaymentMessage is only obtained after HMAC verification.
        var msg = verified.Message;
        var preOrder = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OrderCode == msg.OrderCode, ct)
            ?? throw new BusinessRuleException("ORDER_NOT_FOUND", "Unknown order reference.");
        var pre = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrderId == preOrder.OrderId && p.Provider == "MOCK" &&
                p.AttemptKey == msg.AttemptKey, ct)
            ?? throw new BusinessRuleException("PAYMENT_NOT_FOUND", "Unknown payment attempt.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await SqlServerLocks.ShowtimeAsync(db, preOrder.ShowtimeId, ct);
        var payment = await db.Payments.SingleAsync(p => p.PaymentId == pre.PaymentId, ct);
        var order = await db.Orders.SingleAsync(o => o.OrderId == payment.OrderId, ct);
        var now = clock.UtcNow;

        var seen = await db.PaymentEvents.AnyAsync(e => e.Provider == "MOCK" &&
            e.ProviderEventRef == msg.ProviderEventRef, ct);
        if (seen) return new PaymentResult("DUPLICATE", order.OrderId, order.Status);

        string decision;
        if (order.OrderCode != msg.OrderCode || payment.AmountVnd != msg.AmountVnd ||
            (payment.ProviderTransactionRef is not null && payment.ProviderTransactionRef != msg.ProviderTransactionRef))
        {
            decision = "REJECTED";
        }
        else if (payment.Status is "SUCCESS" or "FAILED" or "NEEDS_REVIEW")
        {
            decision = "DUPLICATE";
        }
        else
        {
            payment.ProviderTransactionRef = msg.ProviderTransactionRef;
            payment.ResolvedUtc = now;
            if (msg.Outcome == "FAILED")
            {
                payment.Status = "FAILED";
                if (order.Status == OrderStates.Pending)
                {
                    order.Status = OrderStates.Failed;
                    await ReleaseOwnedHoldsAsync(order, ct);
                }
                decision = "FAILED_RECORDED";
            }
            else
            {
                var stock = await db.ShowtimeSeats.Where(s => s.ShowtimeId == order.ShowtimeId &&
                    s.OwnerOrderId == order.OrderId).ToListAsync(ct);
                var lines = await db.OrderSeats.Where(s => s.OrderId == order.OrderId).ToListAsync(ct);
                bool eligible = order.Status == OrderStates.Pending && order.ExpiresUtc > now &&
                    stock.Count == order.SeatCount && lines.Count == order.SeatCount &&
                    stock.All(s => s.State == SeatStates.Held && s.HoldExpiresUtc > now) &&
                    stock.Select(s => s.SeatId).Order().SequenceEqual(lines.Select(s => s.SeatId).Order());
                if (eligible)
                {
                    foreach (var s in stock)
                    {
                        s.State = SeatStates.Sold;
                        s.HoldExpiresUtc = null;
                        var tok = qr.Issue();
                        db.Tickets.Add(new Ticket
                        {
                            TicketCode = "TKT-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                            OrderId = order.OrderId, ShowtimeId = order.ShowtimeId,
                            SeatId = s.SeatId, Status = "ISSUED",
                            QrTokenHash = tok.Hash, QrTokenCipher = tok.Cipher,
                            QrKeyId = qr.KeyId, IssuedUtc = now
                        });
                    }
                    order.Status = OrderStates.Paid;
                    order.PaidUtc = now;
                    payment.Status = "SUCCESS";
                    decision = "APPLIED";
                }
                else
                {
                    // No forced EXPIRED->PAID transition. Preserve terminal order history.
                    payment.Status = "NEEDS_REVIEW";
                    if (order.Status == OrderStates.Pending)
                    {
                        order.Status = OrderStates.Review;
                        await ReleaseOwnedHoldsAsync(order, ct);
                    }
                    decision = "NEEDS_REVIEW";
                }
            }
        }
        db.PaymentEvents.Add(new PaymentEvent
        {
            PaymentId = payment.PaymentId, Provider = "MOCK", ProviderEventRef = msg.ProviderEventRef,
            ProviderTransactionRef = msg.ProviderTransactionRef,
            AuthResult = "VERIFIED", Decision = decision,
            PayloadSha256 = MockPaymentSecurity.PayloadHash(msg), ReceivedUtc = now
        });
        db.AuditLogs.Add(Auditing.Event(null, "PAYMENT_" + decision, "Order", order.OrderId.ToString(),
            new { payment.PaymentId, msg.ProviderEventRef, decision }, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new PaymentResult(decision, order.OrderId, order.Status);
    }

    private async Task ReleaseOwnedHoldsAsync(BookingOrder order, CancellationToken ct)
    {
        var holds = await db.ShowtimeSeats.Where(s => s.ShowtimeId == order.ShowtimeId &&
            s.OwnerOrderId == order.OrderId && s.State == SeatStates.Held).ToListAsync(ct);
        foreach (var s in holds)
        {
            s.State = SeatStates.Available;
            s.OwnerOrderId = null;
            s.HoldExpiresUtc = null;
        }
    }
}
