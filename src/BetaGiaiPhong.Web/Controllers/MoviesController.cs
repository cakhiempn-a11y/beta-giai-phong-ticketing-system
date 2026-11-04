using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("movies")]
public sealed class MoviesController(ICatalogService catalogService) : Controller
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly JsonSerializerOptions PreviewJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default
    };

    private const int DefaultCatalogPageSize = 24;

    private static readonly IReadOnlyList<CatalogSelectOption> StandardAgeOptions =
    [
        new("P", "P — Mọi độ tuổi"),
        new("K", "K — Dưới 13 tuổi"),
        new("T13", "T13 — Từ 13 tuổi"),
        new("T16", "T16 — Từ 16 tuổi"),
        new("T18", "T18 — Từ 18 tuổi")
    ];

    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? filter,
        [FromQuery] string? genre,
        [FromQuery] string? age,
        [FromQuery] int? year,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var allPublished = await catalogService.GetPublishedMoviesAsync(ct);

        var normalizedFilter = (filter ?? "all").Trim().ToLowerInvariant();
        if (normalizedFilter is not ("all" or "has-showtimes" or "no-showtimes"))
        {
            normalizedFilter = "all";
        }

        var trimmedQuery = (q ?? string.Empty).Trim();
        var trimmedGenre = (genre ?? string.Empty).Trim();
        var normalizedAge = NormalizeAgeFilter(age);
        var normalizedYear = year is >= 1888 and <= 2100 ? year : null;
        var normalizedSort = NormalizeSortOption(sort);
        var effectivePageSize = pageSize is >= 6 and <= 48 ? pageSize.Value : DefaultCatalogPageSize;

        IEnumerable<MovieSummaryDto> filtered = allPublished;

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            filtered = filtered.Where(m =>
                m.Title.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(m.OriginalTitle) && m.OriginalTitle.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(m.GenreText) && m.GenreText.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(m.DirectorNames) && m.DirectorNames.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(trimmedGenre))
        {
            filtered = filtered.Where(m => MatchesGenre(m.GenreText, trimmedGenre));
        }

        if (!string.IsNullOrWhiteSpace(normalizedAge))
        {
            filtered = filtered.Where(m => MatchesAgeClassification(m.AgeLabel, normalizedAge));
        }

        if (normalizedYear.HasValue)
        {
            filtered = filtered.Where(m => m.ReleaseYear == normalizedYear.Value);
        }

        filtered = normalizedFilter switch
        {
            "has-showtimes" => filtered.Where(m => m.UpcomingShowtimesCount > 0),
            "no-showtimes" => filtered.Where(m => m.UpcomingShowtimesCount == 0),
            _ => filtered
        };

        filtered = normalizedSort switch
        {
            "rating" => filtered
                .OrderByDescending(m => m.Rating != null)
                .ThenByDescending(m => m.Rating?.Score ?? 0m)
                .ThenByDescending(m => m.UpcomingShowtimesCount)
                .ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase),
            "year-desc" => filtered
                .OrderByDescending(m => m.ReleaseYear.HasValue)
                .ThenByDescending(m => m.ReleaseYear ?? 0)
                .ThenByDescending(m => m.Rating?.Score ?? 0m)
                .ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase),
            "showtimes" => filtered
                .OrderByDescending(m => m.UpcomingShowtimesCount)
                .ThenByDescending(m => m.Rating?.Score ?? 0m)
                .ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase),
            "title" => filtered
                .OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered
        };

        var filteredList = filtered.ToList();
        var totalFilteredCount = filteredList.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalFilteredCount / (double)effectivePageSize));
        var currentPage = Math.Clamp(page ?? 1, 1, totalPages);

        var pagedMovies = filteredList
            .Skip((currentPage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToList();

        var previewMap = await catalogService.GetMoviesQuickPreviewMapAsync(pagedMovies.Select(m => m.MovieId), ct);
        var stringKeyMap = previewMap.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

        var availableGenres = allPublished
            .Where(m => !string.IsNullOrWhiteSpace(m.GenreText))
            .SelectMany(m => m.GenreText!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(g => g.Length > 1)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var availableYears = allPublished
            .Where(m => m.ReleaseYear.HasValue)
            .Select(m => m.ReleaseYear!.Value)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        var vm = new MovieCatalogViewModel
        {
            Movies = pagedMovies,
            TotalPublishedCount = allPublished.Count,
            WithShowtimesCount = allPublished.Count(m => m.UpcomingShowtimesCount > 0),
            WithoutShowtimesCount = allPublished.Count(m => m.UpcomingShowtimesCount == 0),
            TotalFilteredCount = totalFilteredCount,
            SearchQuery = trimmedQuery,
            ActiveFilter = normalizedFilter,
            SelectedGenre = trimmedGenre,
            SelectedAge = normalizedAge,
            SelectedYear = normalizedYear,
            SortBy = normalizedSort,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            PageSize = effectivePageSize,
            AvailableGenres = availableGenres,
            AvailableAges = StandardAgeOptions,
            AvailableYears = availableYears,
            QuickPreviewJson = JsonSerializer.Serialize(stringKeyMap, PreviewJsonOptions)
        };

        return View(vm);
    }

    private static string NormalizeAgeFilter(string? rawAge)
    {
        if (string.IsNullOrWhiteSpace(rawAge)) return string.Empty;
        return rawAge.Trim().ToUpperInvariant() switch
        {
            "P" => "P",
            "K" => "K",
            "T13" or "C13" => "T13",
            "T16" or "C16" => "T16",
            "T18" or "C18" => "T18",
            _ => string.Empty
        };
    }

    private static string NormalizeSortOption(string? rawSort)
    {
        if (string.IsNullOrWhiteSpace(rawSort)) return "default";
        return rawSort.Trim().ToLowerInvariant() switch
        {
            "rating" => "rating",
            "year-desc" => "year-desc",
            "showtimes" => "showtimes",
            "title" => "title",
            _ => "default"
        };
    }

    private static bool MatchesGenre(string? movieGenreText, string selectedGenre)
    {
        if (string.IsNullOrWhiteSpace(movieGenreText)) return false;
        var tokens = movieGenreText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(t => string.Equals(t, selectedGenre, StringComparison.OrdinalIgnoreCase))
            || movieGenreText.Contains(selectedGenre, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAgeClassification(string? movieAgeLabel, string normalizedAge)
    {
        if (string.IsNullOrWhiteSpace(movieAgeLabel)) return false;
        return string.Equals(NormalizeAgeFilter(movieAgeLabel), normalizedAge, StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet("{id:long}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Details(long id, [FromQuery] string? date, CancellationToken ct)
    {
        var movie = await catalogService.GetMovieDetailAsync(id, ct);
        if (movie is null)
        {
            return NotFound();
        }

        var showtimeItems = movie.UpcomingShowtimes
            .Select(s =>
            {
                var startUtc = s.StartUtc.Kind == DateTimeKind.Utc
                    ? new DateTimeOffset(s.StartUtc, TimeSpan.Zero)
                    : new DateTimeOffset(DateTime.SpecifyKind(s.StartUtc, DateTimeKind.Utc), TimeSpan.Zero);

                var endUtc = s.EndUtc.Kind == DateTimeKind.Utc
                    ? new DateTimeOffset(s.EndUtc, TimeSpan.Zero)
                    : new DateTimeOffset(DateTime.SpecifyKind(s.EndUtc, DateTimeKind.Utc), TimeSpan.Zero);

                return new ShowtimeItemViewModel
                {
                    Showtime = s,
                    StartVietnam = startUtc.ToOffset(VietnamOffset),
                    EndVietnam = endUtc.ToOffset(VietnamOffset)
                };
            })
            .OrderBy(x => x.StartVietnam)
            .ThenBy(x => x.Showtime.ShowtimeId)
            .ToList();

        var dateGroups = showtimeItems
            .GroupBy(x => DateOnly.FromDateTime(x.StartVietnam.DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new ShowtimeDateGroupViewModel
            {
                DateVietnam = g.Key,
                DayOfWeekLabel = GetVietnameseDayOfWeek(g.Key.DayOfWeek),
                Showtimes = g.OrderBy(x => x.StartVietnam).ThenBy(x => x.Showtime.ShowtimeId).ToList()
            })
            .ToList();

        var groupedLegacy = dateGroups
            .ToDictionary(
                g => g.DateDisplayText,
                g => g.Showtimes.Select(x => x.Showtime).ToList());

        var selectedDateKey = string.Empty;
        if (dateGroups.Count > 0)
        {
            var requestedKey = (date ?? string.Empty).Trim();
            var matchingGroup = dateGroups.FirstOrDefault(g =>
                string.Equals(g.DateKey, requestedKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(g.DateDisplayText, requestedKey, StringComparison.OrdinalIgnoreCase));

            selectedDateKey = (matchingGroup ?? dateGroups[0]).DateKey;
        }

        var allPublished = await catalogService.GetPublishedMoviesAsync(ct);

        var suggestedOnSale = allPublished
            .Where(m => m.MovieId != id && m.UpcomingShowtimesCount > 0)
            .Take(6)
            .ToList();

        var rankedRecommendations = allPublished
            .Where(m => m.MovieId != id)
            .OrderByDescending(m => m.UpcomingShowtimesCount > 0 ? 1 : 0)
            .ThenByDescending(m => m.Rating?.Score ?? 0m)
            .ThenByDescending(m => m.UpcomingShowtimesCount)
            .ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToList();

        var titleSummary = new MovieSummaryDto(
            MovieId: movie.MovieId,
            Title: movie.Title,
            DurationMin: movie.DurationMin,
            GenreText: movie.GenreText,
            Status: movie.Status,
            UpcomingShowtimesCount: movie.UpcomingShowtimes.Count,
            AgeLabel: movie.AgeLabel,
            Synopsis: movie.Synopsis,
            PosterUrl: movie.PosterUrl,
            OriginalTitle: movie.OriginalTitle,
            ReleaseYear: movie.ReleaseYear,
            ReleaseDateVn: movie.ReleaseDateVn,
            OriginCountry: movie.OriginCountry,
            OriginalLanguage: movie.OriginalLanguage,
            DirectorNames: movie.DirectorNames,
            OfficialTrailerYoutubeId: movie.OfficialTrailerYoutubeId,
            IsHeroEligible: movie.IsHeroEligible,
            Rating: movie.Rating,
            Artwork: movie.Artwork,
            Provenance: movie.Provenance);

        var vm = new MovieDetailViewModel
        {
            Movie = movie,
            TitleSummary = titleSummary,
            ShowtimesByDate = groupedLegacy,
            DateGroups = dateGroups,
            SelectedDateKey = selectedDateKey,
            SuggestedOnSaleMovies = suggestedOnSale,
            RankedRecommendations = rankedRecommendations
        };

        return View(vm);
    }

    private static string GetVietnameseDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ 2",
        DayOfWeek.Tuesday => "Thứ 3",
        DayOfWeek.Wednesday => "Thứ 4",
        DayOfWeek.Thursday => "Thứ 5",
        DayOfWeek.Friday => "Thứ 6",
        DayOfWeek.Saturday => "Thứ 7",
        DayOfWeek.Sunday => "Chủ nhật",
        _ => string.Empty
    };
}
