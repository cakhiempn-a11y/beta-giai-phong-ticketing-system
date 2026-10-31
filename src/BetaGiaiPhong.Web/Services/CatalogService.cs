using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<MovieSummaryDto>> GetPublishedMoviesAsync(CancellationToken ct = default);
    Task<MovieDetailDto?> GetMovieDetailAsync(long movieId, CancellationToken ct = default);
    Task<IReadOnlyList<ShowtimeDto>> GetMovieShowtimesAsync(long movieId, CancellationToken ct = default);
    Task<ShowtimeDetailDto?> GetShowtimeDetailAsync(long showtimeId, CancellationToken ct = default);
    Task<SeatMapDto?> GetShowtimeSeatMapAsync(long showtimeId, CancellationToken ct = default);
    Task<IReadOnlyList<ShowtimeDto>> GetUpcomingShowtimesAsync(int limit = 10, CancellationToken ct = default);
}

public sealed class CatalogService(BetaDbContext db, ISystemClock clock) : ICatalogService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static string FormatVietnamTime(DateTime utc)
    {
        var dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
        return dto.ToString("dd/MM/yyyy HH:mm");
    }

    private static string FormatVietnamTimeOnly(DateTime utc)
    {
        var dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
        return dto.ToString("HH:mm");
    }

    public async Task<IReadOnlyList<MovieSummaryDto>> GetPublishedMoviesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var movies = await db.Movies
            .AsNoTracking()
            .Where(m => m.Status == "PUBLISHED")
            .OrderBy(m => m.Title)
            .Select(m => new MovieSummaryDto(
                m.MovieId,
                m.Title,
                m.DurationMin,
                m.GenreText,
                m.Status,
                m.Showtimes.Count(s => s.Status == "ON_SALE" && s.StartUtc > now)
            ))
            .ToListAsync(ct);

        return movies;
    }

    public async Task<MovieDetailDto?> GetMovieDetailAsync(long movieId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var movie = await db.Movies
            .AsNoTracking()
            .Where(m => m.MovieId == movieId && m.Status == "PUBLISHED")
            .Select(m => new
            {
                m.MovieId,
                m.Title,
                m.DurationMin,
                m.GenreText,
                m.Status,
                Showtimes = m.Showtimes
                    .Where(s => s.Status == "ON_SALE" && s.StartUtc > now)
                    .OrderBy(s => s.StartUtc)
                    .Select(s => new
                    {
                        s.ShowtimeId,
                        s.MovieId,
                        MovieTitle = m.Title,
                        s.RoomId,
                        RoomName = s.Room != null ? s.Room.RoomName : "Phòng chiếu",
                        s.StartUtc,
                        s.EndUtc,
                        s.BufferMinutes,
                        s.Status,
                        s.SellableCapacitySnapshot,
                        AvailableCount = s.ShowtimeSeats.Count(ss =>
                            ss.State == SeatStates.Available ||
                            (ss.State == SeatStates.Held && ss.HoldExpiresUtc <= now))
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (movie is null) return null;

        var showtimeDtos = movie.Showtimes.Select(s => new ShowtimeDto(
            s.ShowtimeId,
            s.MovieId,
            s.MovieTitle,
            s.RoomId,
            s.RoomName,
            s.StartUtc,
            s.EndUtc,
            FormatVietnamTime(s.StartUtc),
            FormatVietnamTimeOnly(s.EndUtc),
            s.BufferMinutes,
            s.Status,
            s.SellableCapacitySnapshot,
            s.AvailableCount
        )).ToList();

        return new MovieDetailDto(
            movie.MovieId,
            movie.Title,
            movie.DurationMin,
            movie.GenreText,
            movie.Status,
            showtimeDtos
        );
    }

    public async Task<IReadOnlyList<ShowtimeDto>> GetMovieShowtimesAsync(long movieId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var showtimes = await db.Showtimes
            .AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Include(s => s.ShowtimeSeats)
            .Where(s => s.MovieId == movieId && s.Status == "ON_SALE" && s.StartUtc > now && s.Movie != null && s.Movie.Status == "PUBLISHED")
            .OrderBy(s => s.StartUtc)
            .ToListAsync(ct);

        return showtimes.Select(s => new ShowtimeDto(
            s.ShowtimeId,
            s.MovieId,
            s.Movie?.Title ?? "Phim",
            s.RoomId,
            s.Room?.RoomName ?? "Phòng chiếu",
            s.StartUtc,
            s.EndUtc,
            FormatVietnamTime(s.StartUtc),
            FormatVietnamTimeOnly(s.EndUtc),
            s.BufferMinutes,
            s.Status,
            s.SellableCapacitySnapshot,
            s.ShowtimeSeats.Count(ss =>
                ss.State == SeatStates.Available ||
                (ss.State == SeatStates.Held && ss.HoldExpiresUtc <= now))
        )).ToList();
    }

    public async Task<IReadOnlyList<ShowtimeDto>> GetUpcomingShowtimesAsync(int limit = 10, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var showtimes = await db.Showtimes
            .AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Include(s => s.ShowtimeSeats)
            .Where(s => s.Status == "ON_SALE" && s.StartUtc > now && s.Movie != null && s.Movie.Status == "PUBLISHED")
            .OrderBy(s => s.StartUtc)
            .Take(limit)
            .ToListAsync(ct);

        return showtimes.Select(s => new ShowtimeDto(
            s.ShowtimeId,
            s.MovieId,
            s.Movie?.Title ?? "Phim",
            s.RoomId,
            s.Room?.RoomName ?? "Phòng chiếu",
            s.StartUtc,
            s.EndUtc,
            FormatVietnamTime(s.StartUtc),
            FormatVietnamTimeOnly(s.EndUtc),
            s.BufferMinutes,
            s.Status,
            s.SellableCapacitySnapshot,
            s.ShowtimeSeats.Count(ss =>
                ss.State == SeatStates.Available ||
                (ss.State == SeatStates.Held && ss.HoldExpiresUtc <= now))
        )).ToList();
    }

    public async Task<ShowtimeDetailDto?> GetShowtimeDetailAsync(long showtimeId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var showtime = await db.Showtimes
            .AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Include(s => s.SeatPrices)
            .Include(s => s.ShowtimeSeats)
            .SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);

        if (showtime is null || showtime.Movie is null || showtime.Movie.Status != "PUBLISHED")
            return null;

        var prices = showtime.SeatPrices.ToDictionary(sp => sp.SeatTypeCode, sp => sp.AmountVnd);
        var availableCount = showtime.ShowtimeSeats.Count(ss =>
            ss.State == SeatStates.Available ||
            (ss.State == SeatStates.Held && ss.HoldExpiresUtc <= now));

        return new ShowtimeDetailDto(
            showtime.ShowtimeId,
            showtime.MovieId,
            showtime.Movie.Title,
            showtime.Movie.DurationMin,
            showtime.Movie.GenreText,
            showtime.RoomId,
            showtime.Room?.RoomName ?? "Phòng chiếu",
            showtime.Room?.RoomCode ?? "P_DEMO",
            showtime.StartUtc,
            showtime.EndUtc,
            FormatVietnamTime(showtime.StartUtc),
            FormatVietnamTimeOnly(showtime.EndUtc),
            showtime.Status,
            showtime.SellableCapacitySnapshot,
            availableCount,
            prices
        );
    }

    public async Task<SeatMapDto?> GetShowtimeSeatMapAsync(long showtimeId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var showtime = await db.Showtimes
            .AsNoTracking()
            .Include(s => s.Movie)
            .Include(s => s.Room)
            .Include(s => s.SeatPrices)
            .SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);

        if (showtime is null || showtime.Movie is null || showtime.Movie.Status != "PUBLISHED")
            return null;

        var priceMap = showtime.SeatPrices.ToDictionary(sp => sp.SeatTypeCode, sp => sp.AmountVnd);

        // Fetch seat inventory and physical seats with NoTracking
        var seatData = await (from ss in db.ShowtimeSeats.AsNoTracking()
                              join s in db.Seats.AsNoTracking() on ss.SeatId equals s.SeatId
                              join st in db.SeatTypes.AsNoTracking() on s.SeatTypeCode equals st.SeatTypeCode
                              where ss.ShowtimeId == showtimeId && s.RoomId == showtime.RoomId && ss.RoomId == showtime.RoomId
                              orderby s.RowLabel, s.ColumnNo
                              select new
                              {
                                  ss.SeatId,
                                  s.SeatCode,
                                  s.RowLabel,
                                  s.ColumnNo,
                                  s.SeatTypeCode,
                                  st.TypeName,
                                  s.IsActive,
                                  ss.State,
                                  ss.HoldExpiresUtc
                              }).ToListAsync(ct);

        var seatDtos = new List<SeatDto>(seatData.Count);
        int availableCount = 0;
        int soldCount = 0;
        int heldCount = 0;

        foreach (var item in seatData)
        {
            priceMap.TryGetValue(item.SeatTypeCode, out var priceVnd);

            string displayState;
            bool isBookable;

            if (!item.IsActive || item.State == SeatStates.Blocked)
            {
                displayState = "BLOCKED";
                isBookable = false;
            }
            else if (item.State == SeatStates.Sold)
            {
                displayState = "SOLD";
                isBookable = false;
                soldCount++;
            }
            else if (item.State == SeatStates.Held)
            {
                if (item.HoldExpiresUtc.HasValue && item.HoldExpiresUtc.Value <= now)
                {
                    // Expired hold: practically available for re-reservation
                    displayState = "AVAILABLE";
                    isBookable = true;
                    availableCount++;
                }
                else
                {
                    displayState = "HELD";
                    isBookable = false;
                    heldCount++;
                }
            }
            else
            {
                displayState = "AVAILABLE";
                isBookable = true;
                availableCount++;
            }

            seatDtos.Add(new SeatDto(
                item.SeatId,
                item.SeatCode,
                item.RowLabel,
                item.ColumnNo,
                item.SeatTypeCode,
                item.TypeName,
                priceVnd,
                displayState,
                isBookable
            ));
        }

        return new SeatMapDto(
            showtime.ShowtimeId,
            showtime.MovieId,
            showtime.Movie.Title,
            showtime.RoomId,
            showtime.Room?.RoomName ?? "Phòng chiếu",
            showtime.StartUtc,
            FormatVietnamTime(showtime.StartUtc),
            seatData.Count,
            availableCount,
            soldCount,
            heldCount,
            priceMap,
            seatDtos
        );
    }
}
