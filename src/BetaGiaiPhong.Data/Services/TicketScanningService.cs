using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed record ScanResult(string Outcome, long? TicketId);

public sealed class TicketScanningService(BetaDbContext db, ISystemClock clock)
{
    public async Task<ScanResult> ScanAsync(long staffUserId, string qrToken,
        long expectedShowtimeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(qrToken) || qrToken.Length > 256)
            throw new BusinessRuleException("INVALID_QR", "Invalid QR token encoding.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await SecurityRules.EnsureRoleAsync(db, staffUserId, ct, "STAFF", "MANAGER", "ADMIN");
        var now = clock.UtcNow;
        var ticketId = await SqlServerLocks.TicketByHashAsync(db, QrTokenProtector.HashToken(qrToken), ct);
        if (ticketId is null)
        {
            db.AuditLogs.Add(Auditing.Event(staffUserId, "TICKET_SCAN_REJECTED", "Ticket", "unknown",
                new { reason = "NOT_FOUND" }, now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ScanResult("NOT_FOUND", null);
        }
        var ticket = await db.Tickets.SingleAsync(t => t.TicketId == ticketId.Value, ct);
        var showtime = await db.Showtimes.SingleAsync(s => s.ShowtimeId == ticket.ShowtimeId, ct);
        var order = await db.Orders.SingleAsync(o => o.OrderId == ticket.OrderId, ct);
        string outcome;
        if (ticket.ShowtimeId != expectedShowtimeId) outcome = "WRONG_SHOWTIME";
        else if (now < showtime.StartUtc.AddMinutes(-30) || now > showtime.EndUtc.AddMinutes(15))
            outcome = "OUTSIDE_WINDOW";
        else if (order.Status != OrderStates.Paid) outcome = "REJECTED";
        else if (ticket.Status != "ISSUED") outcome = "ALREADY_USED";
        else
        {
            ticket.Status = "USED";
            ticket.UsedUtc = now;
            outcome = "ACCEPTED";
        }
        db.TicketScans.Add(new TicketScan
        {
            TicketId = ticket.TicketId, StaffUserId = staffUserId,
            ScannedUtc = now, Outcome = outcome
        });
        db.AuditLogs.Add(Auditing.Event(staffUserId, "TICKET_SCAN_" + outcome, "Ticket",
            ticket.TicketId.ToString(), new { expectedShowtimeId }, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ScanResult(outcome, ticket.TicketId);
    }
}
