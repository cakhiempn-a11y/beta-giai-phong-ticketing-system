using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("movies")]
public sealed class MoviesController(ICatalogService catalogService) : Controller
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index([FromQuery] string? q, [FromQuery] string? filter, CancellationToken ct)
    {
        var allPublished = await catalogService.GetPublishedMoviesAsync(ct);

        var normalizedFilter = (filter ?? "all").Trim().ToLowerInvariant();
        if (normalizedFilter is not ("all" or "has-showtimes" or "no-showtimes"))
        {
            normalizedFilter = "all";
        }

        var trimmedQuery = (q ?? string.Empty).Trim();

        IEnumerable<MovieSummaryDto> filtered = allPublished;

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            filtered = filtered.Where(m =>
                m.Title.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(m.GenreText) && m.GenreText.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = normalizedFilter switch
        {
            "has-showtimes" => filtered.Where(m => m.UpcomingShowtimesCount > 0),
            "no-showtimes" => filtered.Where(m => m.UpcomingShowtimesCount == 0),
            _ => filtered
        };

        var vm = new MovieCatalogViewModel
        {
            Movies = filtered.ToList(),
            TotalPublishedCount = allPublished.Count,
            WithShowtimesCount = allPublished.Count(m => m.UpcomingShowtimesCount > 0),
            WithoutShowtimesCount = allPublished.Count(m => m.UpcomingShowtimesCount == 0),
            SearchQuery = trimmedQuery,
            ActiveFilter = normalizedFilter
        };

        return View(vm);
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

        var vm = new MovieDetailViewModel
        {
            Movie = movie,
            ShowtimesByDate = groupedLegacy,
            DateGroups = dateGroups,
            SelectedDateKey = selectedDateKey
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
