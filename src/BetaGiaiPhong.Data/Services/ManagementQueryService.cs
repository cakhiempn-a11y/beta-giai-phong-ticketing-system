using System.Data;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Services;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record MovieManagementSummaryDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string? AgeLabel,
    string? Synopsis,
    string? PosterUrl,
    string Status,
    int TotalShowtimes,
    int ActiveShowtimes);

public sealed record ShowtimeManagementSummaryDto(
    long ShowtimeId,
    long MovieId,
    string MovieTitle,
    long RoomId,
    string RoomName,
    DateTime StartUtc,
    DateTime EndUtc,
    short BufferMinutes,
    string Status,
    int SellableCapacity,
    long TicketsSold,
    decimal RevenueVnd,
    decimal OccupancyPct,
    long? CreatedBy,
    DateTime CreatedUtc);

public sealed record SeatStatusCountDto(int Available, int Held, int Sold, int Blocked);

public sealed record ShowtimeSeatLayoutDto(
    long SeatId,
    string SeatCode,
    string RowLabel,
    short ColumnNo,
    string SeatTypeCode,
    string State,
    DateTime? HoldExpiresUtc,
    long? OwnerOrderId);

public sealed record ShowtimeManagementDetailDto(
    long ShowtimeId,
    long MovieId,
    string MovieTitle,
    short MovieDurationMin,
    long RoomId,
    string RoomName,
    DateTime StartUtc,
    DateTime EndUtc,
    short BufferMinutes,
    string Status,
    int SellableCapacity,
    long TicketsSold,
    decimal RevenueVnd,
    decimal OccupancyPct,
    bool CanCancel,
    IReadOnlyList<SeatPrice> Prices,
    SeatStatusCountDto SeatCounts,
    IReadOnlyList<ShowtimeSeatLayoutDto> Seats);

public sealed record RoomManagementSummaryDto(
    long RoomId,
    string RoomCode,
    string RoomName,
    string Status,
    int TotalSeats,
    int ActiveSeats,
    IReadOnlyList<string> SeatTypes);

public sealed record PhysicalSeatDto(
    long SeatId,
    string SeatCode,
    string RowLabel,
    short ColumnNo,
    string SeatTypeCode,
    bool IsActive);

public sealed record RoomManagementDetailDto(
    long RoomId,
    string RoomCode,
    string RoomName,
    string Status,
    int TotalSeats,
    int ActiveSeats,
    IReadOnlyList<PhysicalSeatDto> Seats);

public sealed record MovieRevenueItemDto(long MovieId, string Title, long TicketsSold, decimal RevenueVnd);
public sealed record RoomRevenueItemDto(long RoomId, string RoomName, long TicketsSold, decimal RevenueVnd);
public sealed record DailyRevenueItemDto(string DateVn, long TicketsSold, decimal RevenueVnd);
public sealed record TopShowtimeItemDto(
    long ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime StartUtc,
    long TicketsSold,
    decimal RevenueVnd,
    decimal OccupancyPct);

public sealed record SalesDashboardReportDto(
    DateTime? FromDateUtc,
    DateTime? ToDateUtc,
    long? FilterMovieId,
    long? FilterRoomId,
    decimal TotalRevenueVnd,
    long TotalTicketsSold,
    int TotalShowtimes,
    decimal WeightedOccupancyPct,
    IReadOnlyList<MovieRevenueItemDto> RevenueByMovie,
    IReadOnlyList<RoomRevenueItemDto> RevenueByRoom,
    IReadOnlyList<DailyRevenueItemDto> RevenueByDay,
    IReadOnlyList<TopShowtimeItemDto> TopShowtimes,
    PagedResult<ShowtimeManagementSummaryDto> ShowtimeDetails);

public sealed class ManagementQueryService(BetaDbContext db, ISystemClock clock)
{
    public async Task<PagedResult<MovieManagementSummaryDto>> GetMoviesAsync(
        string? search, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 5 or > 100) pageSize = 15;

        var nowUtc = clock.UtcNow;
        var query = db.Movies.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(m => m.Title.Contains(s) || (m.GenreText != null && m.GenreText.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            var st = status.Trim().ToUpperInvariant();
            query = query.Where(m => m.Status == st);
        }

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        var items = await query
            .OrderByDescending(m => m.MovieId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MovieManagementSummaryDto(
                m.MovieId,
                m.Title,
                m.DurationMin,
                m.GenreText,
                m.AgeLabel,
                m.Synopsis,
                m.PosterUrl,
                m.Status,
                m.Showtimes.Count,
                m.Showtimes.Count(s => s.Status != "CANCELLED" && s.EndUtc > nowUtc)
            ))
            .ToListAsync(ct);

        return new PagedResult<MovieManagementSummaryDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<Movie?> GetMovieByIdAsync(long movieId, CancellationToken ct = default)
    {
        return await db.Movies
            .Include(m => m.Showtimes.OrderByDescending(s => s.StartUtc))
                .ThenInclude(s => s.Room)
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.MovieId == movieId, ct);
    }

    public async Task<PagedResult<ShowtimeManagementSummaryDto>> GetShowtimesAsync(
        long? movieId, long? roomId, DateTime? fromDateUtc, DateTime? toDateUtc, string? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 5 or > 100) pageSize = 15;

        var baseQuery = from s in db.Showtimes.AsNoTracking()
            join m in db.Movies.AsNoTracking() on s.MovieId equals m.MovieId
            join r in db.Rooms.AsNoTracking() on s.RoomId equals r.RoomId
            join sales in db.ShowtimeSales.AsNoTracking() on s.ShowtimeId equals sales.ShowtimeId into salesGroup
            from sg in salesGroup.DefaultIfEmpty()
            select new
            {
                s.ShowtimeId,
                s.MovieId,
                MovieTitle = m.Title,
                s.RoomId,
                r.RoomName,
                s.StartUtc,
                s.EndUtc,
                s.BufferMinutes,
                s.Status,
                SellableCapacity = s.SellableCapacitySnapshot ?? ((int?)sg.SellableCapacity ?? 0),
                TicketsSold = (long?)sg.TicketsSold ?? 0L,
                RevenueVnd = (decimal?)sg.RevenueVnd ?? 0m,
                OccupancyPct = (decimal?)sg.OccupancyPct ?? 0m,
                s.CreatedBy,
                s.CreatedUtc
            };

        if (movieId.HasValue && movieId.Value > 0)
            baseQuery = baseQuery.Where(s => s.MovieId == movieId.Value);

        if (roomId.HasValue && roomId.Value > 0)
            baseQuery = baseQuery.Where(s => s.RoomId == roomId.Value);

        if (fromDateUtc.HasValue)
            baseQuery = baseQuery.Where(s => s.StartUtc >= fromDateUtc.Value);

        if (toDateUtc.HasValue)
            baseQuery = baseQuery.Where(s => s.StartUtc <= toDateUtc.Value);

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            var st = status.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(s => s.Status == st);
        }

        var totalCount = await baseQuery.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        var itemsRaw = await baseQuery
            .OrderByDescending(s => s.StartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = itemsRaw.Select(s => new ShowtimeManagementSummaryDto(
            s.ShowtimeId, s.MovieId, s.MovieTitle, s.RoomId, s.RoomName,
            s.StartUtc, s.EndUtc, s.BufferMinutes, s.Status, s.SellableCapacity,
            s.TicketsSold, s.RevenueVnd, s.OccupancyPct, s.CreatedBy, s.CreatedUtc
        )).ToList();

        return new PagedResult<ShowtimeManagementSummaryDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<ShowtimeManagementDetailDto?> GetShowtimeDetailsAsync(long showtimeId, CancellationToken ct = default)
    {
        var showtime = await db.Showtimes
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Include(s => s.SeatPrices)
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);

        if (showtime == null) return null;

        var sales = await db.ShowtimeSales.AsNoTracking()
            .SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);

        var seats = await (from ss in db.ShowtimeSeats.AsNoTracking()
            join seat in db.Seats.AsNoTracking() on ss.SeatId equals seat.SeatId
            where ss.ShowtimeId == showtimeId
            orderby seat.RowLabel, seat.ColumnNo
            select new ShowtimeSeatLayoutDto(
                seat.SeatId,
                seat.SeatCode,
                seat.RowLabel,
                seat.ColumnNo,
                seat.SeatTypeCode,
                ss.State,
                ss.HoldExpiresUtc,
                ss.OwnerOrderId
            )).ToListAsync(ct);

        var available = seats.Count(s => s.State == SeatStates.Available);
        var held = seats.Count(s => s.State == SeatStates.Held);
        var sold = seats.Count(s => s.State == SeatStates.Sold);
        var blocked = seats.Count(s => s.State == SeatStates.Blocked);
        var counts = new SeatStatusCountDto(available, held, sold, blocked);

        var ticketsSold = sales?.TicketsSold ?? sold;
        var revenueVnd = sales?.RevenueVnd ?? 0m;
        var capacity = showtime.SellableCapacitySnapshot ?? sales?.SellableCapacity ?? seats.Count;
        var occupancyPct = sales?.OccupancyPct ?? (capacity > 0 ? Math.Round((decimal)ticketsSold * 100m / capacity, 2) : 0m);

        // Can cancel only if ON_SALE, zero tickets sold, and zero seats currently SOLD
        var canCancel = showtime.Status == "ON_SALE" && ticketsSold == 0 && sold == 0;

        return new ShowtimeManagementDetailDto(
            showtime.ShowtimeId,
            showtime.MovieId,
            showtime.Movie?.Title ?? "N/A",
            showtime.Movie?.DurationMin ?? 0,
            showtime.RoomId,
            showtime.Room?.RoomName ?? "N/A",
            showtime.StartUtc,
            showtime.EndUtc,
            showtime.BufferMinutes,
            showtime.Status,
            capacity,
            ticketsSold,
            revenueVnd,
            occupancyPct,
            canCancel,
            showtime.SeatPrices.ToList(),
            counts,
            seats
        );
    }

    public async Task CloseShowtimeAsync(long managerId, long showtimeId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var showtime = await db.Showtimes.SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct)
            ?? throw new BusinessRuleException("SHOWTIME_NOT_FOUND", "Không tìm thấy suất chiếu yêu cầu.");

        if (showtime.Status == "CLOSED")
            return;

        showtime.Status = "CLOSED";
        var now = clock.UtcNow;
        db.AuditLogs.Add(Auditing.Event(managerId, "SHOWTIME_CLOSED", "Showtime",
            showtime.ShowtimeId.ToString(), new { showtime.ShowtimeId }, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelShowtimeAsync(long managerId, long showtimeId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await SqlServerLocks.ShowtimeAsync(db, showtimeId, ct))
            throw new BusinessRuleException("SHOWTIME_NOT_FOUND", "Không tìm thấy suất chiếu yêu cầu.");

        await SecurityRules.EnsureRoleAsync(db, managerId, ct, "MANAGER", "ADMIN");

        var showtime = await db.Showtimes.SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct)
            ?? throw new BusinessRuleException("SHOWTIME_NOT_FOUND", "Không tìm thấy suất chiếu yêu cầu.");

        if (showtime.Status == "CANCELLED")
            return;

        // Strict guard: Cannot cancel showtime if tickets have been issued or paid orders exist
        var hasTickets = await db.Tickets.AnyAsync(t => t.ShowtimeId == showtimeId, ct);
        var hasSoldSeats = await db.ShowtimeSeats.AnyAsync(ss => ss.ShowtimeId == showtimeId && ss.State == SeatStates.Sold, ct);
        var hasPaidOrders = await db.Orders.AnyAsync(o => o.ShowtimeId == showtimeId && o.Status == OrderStates.Paid, ct);

        if (hasTickets || hasSoldSeats || hasPaidOrders)
        {
            throw new BusinessRuleException("CANNOT_CANCEL_SHOWTIME",
                "Không thể hủy suất chiếu đã phát hành vé hoặc có đơn hàng thanh toán thành công.");
        }

        // Release any pending held seats back to AVAILABLE
        var heldSeats = await db.ShowtimeSeats
            .Where(ss => ss.ShowtimeId == showtimeId && ss.State == SeatStates.Held)
            .ToListAsync(ct);

        foreach (var hs in heldSeats)
        {
            hs.State = SeatStates.Available;
            hs.OwnerOrderId = null;
            hs.HoldExpiresUtc = null;
        }

        showtime.Status = "CANCELLED";
        var now = clock.UtcNow;
        db.AuditLogs.Add(Auditing.Event(managerId, "SHOWTIME_CANCELLED", "Showtime",
            showtime.ShowtimeId.ToString(), new { showtime.ShowtimeId, ReleasedHeldSeats = heldSeats.Count }, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<RoomManagementSummaryDto>> GetRoomsAsync(CancellationToken ct = default)
    {
        var rooms = await db.Rooms
            .Include(r => r.Seats)
            .AsNoTracking()
            .OrderBy(r => r.RoomId)
            .ToListAsync(ct);

        return rooms.Select(r => new RoomManagementSummaryDto(
            r.RoomId,
            r.RoomCode,
            r.RoomName,
            r.Status,
            r.Seats.Count,
            r.Seats.Count(s => s.IsActive),
            r.Seats.Where(s => s.IsActive).Select(s => s.SeatTypeCode).Distinct().ToList()
        )).ToList();
    }

    public async Task<RoomManagementDetailDto?> GetRoomDetailsWithSeatLayoutAsync(long roomId, CancellationToken ct = default)
    {
        var room = await db.Rooms
            .Include(r => r.Seats.OrderBy(s => s.RowLabel).ThenBy(s => s.ColumnNo))
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.RoomId == roomId, ct);

        if (room == null) return null;

        var seats = room.Seats.Select(s => new PhysicalSeatDto(
            s.SeatId, s.SeatCode, s.RowLabel, s.ColumnNo, s.SeatTypeCode, s.IsActive
        )).ToList();

        return new RoomManagementDetailDto(
            room.RoomId,
            room.RoomCode,
            room.RoomName,
            room.Status,
            seats.Count,
            seats.Count(s => s.IsActive),
            seats
        );
    }

    public async Task<SalesDashboardReportDto> GetSalesDashboardAsync(
        DateTime? fromDateUtc, DateTime? toDateUtc, long? movieId, long? roomId,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 5 or > 100) pageSize = 15;

        var salesQuery = db.ShowtimeSales.AsNoTracking().AsQueryable();

        if (fromDateUtc.HasValue)
            salesQuery = salesQuery.Where(s => s.StartUtc >= fromDateUtc.Value);

        if (toDateUtc.HasValue)
            salesQuery = salesQuery.Where(s => s.StartUtc <= toDateUtc.Value);

        if (movieId.HasValue && movieId.Value > 0)
            salesQuery = salesQuery.Where(s => s.MovieId == movieId.Value);

        if (roomId.HasValue && roomId.Value > 0)
            salesQuery = salesQuery.Where(s => s.RoomId == roomId.Value);

        // Core Aggregate KPIs
        var allSales = await salesQuery.ToListAsync(ct);

        decimal totalRevenue = allSales.Sum(s => s.RevenueVnd);
        long totalTickets = allSales.Sum(s => s.TicketsSold);
        int totalShowtimes = allSales.Count;
        int totalCapacity = allSales.Sum(s => s.SellableCapacity);

        decimal weightedOccupancy = totalCapacity > 0
            ? Math.Round((decimal)totalTickets * 100m / totalCapacity, 2)
            : 0m;

        // Revenue by Movie
        var movieIds = allSales.Select(s => s.MovieId).Distinct().ToList();
        var moviesMap = await db.Movies.AsNoTracking()
            .Where(m => movieIds.Contains(m.MovieId))
            .ToDictionaryAsync(m => m.MovieId, m => m.Title, ct);

        var revenueByMovie = allSales
            .GroupBy(s => s.MovieId)
            .Select(g => new MovieRevenueItemDto(
                g.Key,
                moviesMap.TryGetValue(g.Key, out var title) ? title : $"Phim #{g.Key}",
                g.Sum(s => s.TicketsSold),
                g.Sum(s => s.RevenueVnd)
            ))
            .OrderByDescending(r => r.RevenueVnd)
            .ToList();

        // Revenue by Room
        var roomIds = allSales.Select(s => s.RoomId).Distinct().ToList();
        var roomsMap = await db.Rooms.AsNoTracking()
            .Where(r => roomIds.Contains(r.RoomId))
            .ToDictionaryAsync(r => r.RoomId, r => r.RoomName, ct);

        var revenueByRoom = allSales
            .GroupBy(s => s.RoomId)
            .Select(g => new RoomRevenueItemDto(
                g.Key,
                roomsMap.TryGetValue(g.Key, out var name) ? name : $"Phòng #{g.Key}",
                g.Sum(s => s.TicketsSold),
                g.Sum(s => s.RevenueVnd)
            ))
            .OrderByDescending(r => r.RevenueVnd)
            .ToList();

        // Revenue by Day (Vietnam Time UTC+7)
        var revenueByDay = allSales
            .GroupBy(s => s.StartUtc.AddHours(7).ToString("yyyy-MM-dd"))
            .Select(g => new DailyRevenueItemDto(
                g.Key,
                g.Sum(s => s.TicketsSold),
                g.Sum(s => s.RevenueVnd)
            ))
            .OrderBy(r => r.DateVn)
            .ToList();

        // Top Showtimes
        var topShowtimes = allSales
            .OrderByDescending(s => s.RevenueVnd)
            .ThenByDescending(s => s.TicketsSold)
            .Take(5)
            .Select(s => new TopShowtimeItemDto(
                s.ShowtimeId,
                moviesMap.TryGetValue(s.MovieId, out var mTitle) ? mTitle : $"Phim #{s.MovieId}",
                roomsMap.TryGetValue(s.RoomId, out var rName) ? rName : $"Phòng #{s.RoomId}",
                s.StartUtc,
                s.TicketsSold,
                s.RevenueVnd,
                s.OccupancyPct
            ))
            .ToList();

        // Paginated Showtimes
        var pagedShowtimes = await GetShowtimesAsync(
            movieId, roomId, fromDateUtc, toDateUtc, null, page, pageSize, ct);

        return new SalesDashboardReportDto(
            fromDateUtc,
            toDateUtc,
            movieId,
            roomId,
            totalRevenue,
            totalTickets,
            totalShowtimes,
            weightedOccupancy,
            revenueByMovie,
            revenueByRoom,
            revenueByDay,
            topShowtimes,
            pagedShowtimes
        );
    }
}
