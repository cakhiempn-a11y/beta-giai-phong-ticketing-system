using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.ViewModels;

namespace BetaGiaiPhong.Web.Controllers;

[Authorize(Policy = "RequireStaff")]
[Route("staff/check-in")]
public sealed class StaffCheckInController(BetaDbContext db, ISystemClock clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] long? showtimeId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var showtimes = await db.Showtimes.AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Where(s => s.EndUtc >= now.AddHours(-4) && s.StartUtc <= now.AddDays(2))
            .OrderBy(s => s.StartUtc)
            .Select(s => new StaffCheckInShowtimeViewModel(
                s.ShowtimeId,
                s.Movie != null ? s.Movie.Title : "N/A",
                s.Room != null ? s.Room.RoomName : "N/A",
                s.StartUtc,
                s.EndUtc,
                s.Status,
                db.ShowtimeSeats.Count(ss => ss.ShowtimeId == s.ShowtimeId && ss.State == "SOLD"),
                s.SellableCapacitySnapshot
            ))
            .ToListAsync(ct);

        var vm = new StaffCheckInIndexViewModel
        {
            SelectedShowtimeId = showtimeId ?? showtimes.FirstOrDefault()?.ShowtimeId,
            Showtimes = showtimes
        };

        return View(vm);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] long? showtimeId,
        [FromQuery] string? outcome,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 5 or > 100) pageSize = 20;

        var normalizedOutcome = outcome?.Trim().ToUpperInvariant();

        var scansQuery = from ts in db.TicketScans.AsNoTracking()
            join t in db.Tickets.AsNoTracking() on ts.TicketId equals t.TicketId
            join s in db.Seats.AsNoTracking() on t.SeatId equals s.SeatId
            join st in db.Showtimes.AsNoTracking() on t.ShowtimeId equals st.ShowtimeId
            join m in db.Movies.AsNoTracking() on st.MovieId equals m.MovieId
            join r in db.Rooms.AsNoTracking() on st.RoomId equals r.RoomId
            join u in db.Users.AsNoTracking() on ts.StaffUserId equals u.UserId
            where (!showtimeId.HasValue || t.ShowtimeId == showtimeId.Value)
               && (string.IsNullOrEmpty(normalizedOutcome) || ts.Outcome == normalizedOutcome)
            select new StaffScanHistoryItemViewModel
            {
                Id = ts.ScanId,
                ScannedUtc = ts.ScannedUtc,
                StaffName = u.DisplayName ?? u.Email,
                TicketCode = t.TicketCode,
                MovieTitle = m.Title,
                RoomName = r.RoomName,
                SeatLabel = s.RowLabel + s.ColumnNo.ToString(),
                Outcome = ts.Outcome,
                Details = "Vé hợp lệ"
            };

        IQueryable<StaffScanHistoryItemViewModel> combinedQuery = scansQuery;

        // When not filtering by a specific showtime, include NOT_FOUND events from AuditLogs
        if (!showtimeId.HasValue && (string.IsNullOrEmpty(normalizedOutcome) || normalizedOutcome == "NOT_FOUND"))
        {
            var auditQuery = from al in db.AuditLogs.AsNoTracking()
                where al.EventType == "TICKET_SCAN_REJECTED"
                join u in db.Users.AsNoTracking() on al.ActorUserId equals u.UserId into staffUsers
                from u in staffUsers.DefaultIfEmpty()
                select new StaffScanHistoryItemViewModel
                {
                    Id = al.AuditId,
                    ScannedUtc = al.CreatedUtc,
                    StaffName = u != null ? (u.DisplayName ?? u.Email) : "Nhân viên",
                    TicketCode = null,
                    MovieTitle = "Không xác định",
                    RoomName = null,
                    SeatLabel = null,
                    Outcome = "NOT_FOUND",
                    Details = "Mã QR không tồn tại"
                };

            if (normalizedOutcome == "NOT_FOUND")
            {
                combinedQuery = auditQuery;
            }
            else
            {
                combinedQuery = scansQuery.Concat(auditQuery);
            }
        }

        var totalCount = await combinedQuery.CountAsync(ct);
        var items = await combinedQuery
            .OrderByDescending(x => x.ScannedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var filterShowtimes = await db.Showtimes.AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Where(s => s.EndUtc >= now.AddHours(-12) && s.StartUtc <= now.AddDays(2))
            .OrderBy(s => s.StartUtc)
            .Select(s => new StaffCheckInShowtimeViewModel(
                s.ShowtimeId,
                s.Movie != null ? s.Movie.Title : "N/A",
                s.Room != null ? s.Room.RoomName : "N/A",
                s.StartUtc,
                s.EndUtc,
                s.Status,
                db.ShowtimeSeats.Count(ss => ss.ShowtimeId == s.ShowtimeId && ss.State == "SOLD"),
                s.SellableCapacitySnapshot
            ))
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        var vm = new StaffCheckInHistoryViewModel
        {
            Items = items,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
            SelectedShowtimeId = showtimeId,
            SelectedOutcome = outcome,
            Showtimes = filterShowtimes
        };

        return View(vm);
    }
}
