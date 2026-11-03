using System.Globalization;
using System.Text.Json;
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
    Task<MovieQuickPreviewDto?> GetMovieQuickPreviewAsync(long movieId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<long, MovieQuickPreviewDto>> GetMoviesQuickPreviewMapAsync(IEnumerable<long> movieIds, CancellationToken ct = default);
    Task<IReadOnlyList<ShowtimeDto>> GetMovieShowtimesAsync(long movieId, CancellationToken ct = default);
    Task<ShowtimeDetailDto?> GetShowtimeDetailAsync(long showtimeId, CancellationToken ct = default);
    Task<SeatMapDto?> GetShowtimeSeatMapAsync(long showtimeId, CancellationToken ct = default);
    Task<IReadOnlyList<ShowtimeDto>> GetUpcomingShowtimesAsync(int limit = 10, CancellationToken ct = default);
}

public sealed class CatalogService(BetaDbContext db, ISystemClock clock) : ICatalogService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly JsonSerializerOptions CastJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    internal static MovieRatingDto? BuildRatingDto(
        string? source,
        decimal? score,
        decimal? scaleMax,
        int? voteCount,
        DateTime? fetchedUtc)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            !score.HasValue ||
            !scaleMax.HasValue ||
            scaleMax.Value <= 0m ||
            !voteCount.HasValue ||
            voteCount.Value <= 0)
        {
            return null;
        }

        var normalizedSource = string.Equals(source, "IMDB", StringComparison.OrdinalIgnoreCase)
            ? "IMDb"
            : source.Trim().ToUpperInvariant();

        var scoreText = score.Value.ToString("0.0", CultureInfo.InvariantCulture);
        var scaleText = scaleMax.Value % 1m == 0m
            ? ((int)scaleMax.Value).ToString(CultureInfo.InvariantCulture)
            : scaleMax.Value.ToString("0.0", CultureInfo.InvariantCulture);
        var votesText = voteCount.Value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var displayText = $"{normalizedSource} {scoreText}/{scaleText} ({votesText} lượt)";

        return new MovieRatingDto(
            source.Trim().ToUpperInvariant(),
            score.Value,
            scaleMax.Value,
            voteCount.Value,
            fetchedUtc,
            displayText);
    }

    internal static MovieArtworkDto BuildArtworkDto(Movie m)
    {
        var resolvedSource = string.IsNullOrWhiteSpace(m.ArtworkSource)
            ? (!string.IsNullOrWhiteSpace(m.PosterUrl) || !string.IsNullOrWhiteSpace(m.BackdropUrl) ? "UNSPECIFIED" : "NONE")
            : m.ArtworkSource.Trim().ToUpperInvariant();

        bool hasVerifiedPoster = !string.IsNullOrWhiteSpace(m.PosterUrl) &&
            !m.PosterUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolvedSource, "NONE", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolvedSource, "PENDING_TMDB_API_KEY", StringComparison.OrdinalIgnoreCase);

        bool hasVerifiedBackdrop = !string.IsNullOrWhiteSpace(m.BackdropUrl) &&
            !m.BackdropUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolvedSource, "NONE", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolvedSource, "PENDING_TMDB_API_KEY", StringComparison.OrdinalIgnoreCase);

        return new MovieArtworkDto(
            m.PosterUrl,
            m.PosterW342Url ?? m.PosterUrl,
            m.BackdropUrl,
            m.BackdropW780Url ?? m.BackdropUrl,
            m.TitleLogoUrl,
            resolvedSource,
            m.ArtworkLicenseText,
            m.ArtworkAttributionText,
            hasVerifiedPoster,
            hasVerifiedBackdrop);
    }

    private static MovieProvenanceDto BuildProvenanceDto(Movie m) => new(
        m.TmdbId,
        m.ImdbId,
        m.WikidataQid,
        m.MetadataSource,
        m.AgeLabelMarket,
        m.AgeLabelSource,
        m.ExternalSyncedUtc,
        m.ExternalExpiresUtc);

    private static IReadOnlyList<MovieCastMemberDto> ParseCastList(string? castJson)
    {
        if (string.IsNullOrWhiteSpace(castJson))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<MovieCastMemberDto>>(castJson, CastJsonOptions);
            if (items is null || items.Count == 0)
            {
                return [];
            }

            return items
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? BuildShortSynopsis(string? synopsis, int maxChars = 190)
    {
        if (string.IsNullOrWhiteSpace(synopsis))
        {
            return null;
        }

        var trimmed = synopsis.Trim();
        if (trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        var cut = trimmed.Substring(0, maxChars);
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 80)
        {
            cut = cut.Substring(0, lastSpace);
        }

        return cut.TrimEnd(',', '.', ';', ' ') + "…";
    }

    public async Task<IReadOnlyList<MovieSummaryDto>> GetPublishedMoviesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var rows = await db.Movies
            .AsNoTracking()
            .Where(m => m.Status == "PUBLISHED")
            .Select(m => new
            {
                Movie = m,
                UpcomingCount = m.Showtimes.Count(s => s.Status == "ON_SALE" && s.StartUtc > now),
                NextStartUtc = m.Showtimes
                    .Where(s => s.Status == "ON_SALE" && s.StartUtc > now)
                    .OrderBy(s => s.StartUtc)
                    .Select(s => (DateTime?)s.StartUtc)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => string.IsNullOrWhiteSpace(r.Movie.PosterUrl) ? 1 : 0)
            .ThenByDescending(r => r.UpcomingCount > 0 ? 1 : 0)
            .ThenBy(r => r.NextStartUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Movie.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(r =>
            {
                var m = r.Movie;
                return new MovieSummaryDto(
                    MovieId: m.MovieId,
                    Title: m.Title,
                    DurationMin: m.DurationMin,
                    GenreText: m.GenreText,
                    Status: m.Status,
                    UpcomingShowtimesCount: r.UpcomingCount,
                    AgeLabel: m.AgeLabel,
                    Synopsis: m.Synopsis,
                    PosterUrl: m.PosterUrl,
                    OriginalTitle: m.OriginalTitle,
                    ReleaseYear: m.ReleaseYear,
                    ReleaseDateVn: m.ReleaseDateVn,
                    OriginCountry: m.OriginCountry,
                    OriginalLanguage: m.OriginalLanguage,
                    DirectorNames: m.DirectorNames,
                    OfficialTrailerYoutubeId: m.OfficialTrailerYoutubeId,
                    IsHeroEligible: m.IsHeroEligible,
                    Rating: BuildRatingDto(m.RatingSource, m.RatingValue, m.RatingScaleMax, m.RatingVoteCount, m.RatingFetchedUtc),
                    Artwork: BuildArtworkDto(m),
                    Provenance: BuildProvenanceDto(m));
            })
            .ToList();
    }

    public async Task<MovieDetailDto?> GetMovieDetailAsync(long movieId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var row = await db.Movies
            .AsNoTracking()
            .Where(m => m.MovieId == movieId && m.Status == "PUBLISHED")
            .Select(m => new
            {
                Movie = m,
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

        if (row is null) return null;

        var m = row.Movie;
        var showtimeDtos = row.Showtimes.Select(s => new ShowtimeDto(
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
            MovieId: m.MovieId,
            Title: m.Title,
            DurationMin: m.DurationMin,
            GenreText: m.GenreText,
            Status: m.Status,
            UpcomingShowtimes: showtimeDtos,
            AgeLabel: m.AgeLabel,
            Synopsis: m.Synopsis,
            PosterUrl: m.PosterUrl,
            OriginalTitle: m.OriginalTitle,
            ReleaseYear: m.ReleaseYear,
            ReleaseDateVn: m.ReleaseDateVn,
            OriginCountry: m.OriginCountry,
            OriginalLanguage: m.OriginalLanguage,
            DirectorNames: m.DirectorNames,
            OfficialTrailerYoutubeId: m.OfficialTrailerYoutubeId,
            IsHeroEligible: m.IsHeroEligible,
            Rating: BuildRatingDto(m.RatingSource, m.RatingValue, m.RatingScaleMax, m.RatingVoteCount, m.RatingFetchedUtc),
            Artwork: BuildArtworkDto(m),
            Provenance: BuildProvenanceDto(m),
            Cast: ParseCastList(m.CastJson));
    }

    public async Task<MovieQuickPreviewDto?> GetMovieQuickPreviewAsync(long movieId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var row = await db.Movies
            .AsNoTracking()
            .Where(m => m.MovieId == movieId && m.Status == "PUBLISHED")
            .Select(m => new
            {
                Movie = m,
                TotalUpcoming = m.Showtimes.Count(s => s.Status == "ON_SALE" && s.StartUtc > now),
                NextShowtimes = m.Showtimes
                    .Where(s => s.Status == "ON_SALE" && s.StartUtc > now)
                    .OrderBy(s => s.StartUtc)
                    .Take(4)
                    .Select(s => new
                    {
                        s.ShowtimeId,
                        s.StartUtc,
                        s.EndUtc,
                        RoomName = s.Room != null ? s.Room.RoomName : "Phòng chiếu",
                        AvailableCount = s.ShowtimeSeats.Count(ss =>
                            ss.State == SeatStates.Available ||
                            (ss.State == SeatStates.Held && ss.HoldExpiresUtc <= now))
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var m = row.Movie;
        var chips = row.NextShowtimes
            .Select(s =>
            {
                var startVn = new DateTimeOffset(DateTime.SpecifyKind(s.StartUtc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
                var endVn = new DateTimeOffset(DateTime.SpecifyKind(s.EndUtc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
                return new QuickPreviewShowtimeChipDto(
                    s.ShowtimeId,
                    FormatVietnamTime(s.StartUtc),
                    FormatVietnamTimeOnly(s.StartUtc),
                    s.RoomName,
                    s.AvailableCount,
                    endVn.Date > startVn.Date);
            })
            .ToList();

        return new MovieQuickPreviewDto(
            MovieId: m.MovieId,
            Title: m.Title,
            OriginalTitle: m.OriginalTitle,
            ReleaseYear: m.ReleaseYear,
            DurationMin: m.DurationMin,
            GenreText: m.GenreText,
            AgeLabel: m.AgeLabel,
            AgeLabelMarket: m.AgeLabelMarket,
            DirectorNames: m.DirectorNames,
            ShortSynopsis: BuildShortSynopsis(m.Synopsis),
            OfficialTrailerYoutubeId: m.OfficialTrailerYoutubeId,
            UpcomingShowtimesCount: row.TotalUpcoming,
            NextShowtimes: chips,
            Rating: BuildRatingDto(m.RatingSource, m.RatingValue, m.RatingScaleMax, m.RatingVoteCount, m.RatingFetchedUtc),
            Artwork: BuildArtworkDto(m));
    }

    public async Task<IReadOnlyDictionary<long, MovieQuickPreviewDto>> GetMoviesQuickPreviewMapAsync(
        IEnumerable<long> movieIds,
        CancellationToken ct = default)
    {
        var ids = movieIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<long, MovieQuickPreviewDto>();
        }

        var now = clock.UtcNow;
        var rows = await db.Movies
            .AsNoTracking()
            .Where(m => ids.Contains(m.MovieId) && m.Status == "PUBLISHED")
            .Select(m => new
            {
                Movie = m,
                TotalUpcoming = m.Showtimes.Count(s => s.Status == "ON_SALE" && s.StartUtc > now),
                NextShowtimes = m.Showtimes
                    .Where(s => s.Status == "ON_SALE" && s.StartUtc > now)
                    .OrderBy(s => s.StartUtc)
                    .Take(4)
                    .Select(s => new
                    {
                        s.ShowtimeId,
                        s.StartUtc,
                        s.EndUtc,
                        RoomName = s.Room != null ? s.Room.RoomName : "Phòng chiếu",
                        AvailableCount = s.ShowtimeSeats.Count(ss =>
                            ss.State == SeatStates.Available ||
                            (ss.State == SeatStates.Held && ss.HoldExpiresUtc <= now))
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        var dict = new Dictionary<long, MovieQuickPreviewDto>(rows.Count);
        foreach (var row in rows)
        {
            var m = row.Movie;
            var chips = row.NextShowtimes
                .Select(s =>
                {
                    var startVn = new DateTimeOffset(DateTime.SpecifyKind(s.StartUtc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
                    var endVn = new DateTimeOffset(DateTime.SpecifyKind(s.EndUtc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
                    return new QuickPreviewShowtimeChipDto(
                        s.ShowtimeId,
                        FormatVietnamTime(s.StartUtc),
                        FormatVietnamTimeOnly(s.StartUtc),
                        s.RoomName,
                        s.AvailableCount,
                        endVn.Date > startVn.Date);
                })
                .ToList();

            dict[m.MovieId] = new MovieQuickPreviewDto(
                MovieId: m.MovieId,
                Title: m.Title,
                OriginalTitle: m.OriginalTitle,
                ReleaseYear: m.ReleaseYear,
                DurationMin: m.DurationMin,
                GenreText: m.GenreText,
                AgeLabel: m.AgeLabel,
                AgeLabelMarket: m.AgeLabelMarket,
                DirectorNames: m.DirectorNames,
                ShortSynopsis: BuildShortSynopsis(m.Synopsis),
                OfficialTrailerYoutubeId: m.OfficialTrailerYoutubeId,
                UpcomingShowtimesCount: row.TotalUpcoming,
                NextShowtimes: chips,
                Rating: BuildRatingDto(m.RatingSource, m.RatingValue, m.RatingScaleMax, m.RatingVoteCount, m.RatingFetchedUtc),
                Artwork: BuildArtworkDto(m));
        }

        return dict;
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

        var isOpenForBooking = showtime.Status == "ON_SALE" && showtime.StartUtc > now;
        string? ineligibleReason = null;
        if (showtime.Status != "ON_SALE")
        {
            ineligibleReason = "Suất chiếu hiện không mở bán vé trực tuyến.";
        }
        else if (showtime.StartUtc <= now)
        {
            ineligibleReason = "Suất chiếu đã bắt đầu hoặc đã qua thời gian mở bán vé.";
        }

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
                    // Expired hold: practically available for re-reservation if showtime is open
                    displayState = "AVAILABLE";
                    isBookable = isOpenForBooking;
                    if (isBookable) availableCount++;
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
                isBookable = isOpenForBooking;
                if (isBookable) availableCount++;
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

        var startVn = new DateTimeOffset(DateTime.SpecifyKind(showtime.StartUtc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
        var endVn = new DateTimeOffset(DateTime.SpecifyKind(showtime.EndUtc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
        var m = showtime.Movie;

        return new SeatMapDto(
            showtime.ShowtimeId,
            showtime.MovieId,
            m.Title,
            showtime.RoomId,
            showtime.Room?.RoomName ?? "Phòng chiếu",
            showtime.StartUtc,
            FormatVietnamTime(showtime.StartUtc),
            seatData.Count,
            availableCount,
            soldCount,
            heldCount,
            priceMap,
            seatDtos,
            isOpenForBooking,
            ineligibleReason,
            showtime.EndUtc,
            FormatVietnamTimeOnly(showtime.EndUtc),
            endVn.Date > startVn.Date,
            showtime.Status,
            m.DurationMin,
            m.GenreText,
            m.AgeLabel,
            m.AgeLabelMarket,
            m.OriginalTitle,
            m.ReleaseYear,
            BuildRatingDto(m.RatingSource, m.RatingValue, m.RatingScaleMax, m.RatingVoteCount, m.RatingFetchedUtc),
            BuildArtworkDto(m)
        );
    }
}
