using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Middleware;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

public sealed class HomeController(ICatalogService catalogService) : Controller
{
    private static readonly JsonSerializerOptions PreviewJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default
    };

    [HttpGet("")]
    [HttpGet("home")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var movies = await catalogService.GetPublishedMoviesAsync(ct);
        var upcomingShowtimes = await catalogService.GetUpcomingShowtimesAsync(8, ct);
        var previewMap = await catalogService.GetMoviesQuickPreviewMapAsync(movies.Select(m => m.MovieId), ct);

        // Deterministic Grounded Hero Movie Selection:
        // 1. Prefer Hero-eligible published movies (verified BackdropUrl + PosterUrl + Synopsis) that have upcoming showtimes,
        //    ranking movies with an official trailer and rich multi-slot schedule first.
        // 2. Fallback to the first Hero-eligible published movie.
        // 3. Movies lacking real backdrop/poster/synopsis (e.g. PENDING_TMDB_API_KEY) are strictly excluded from Hero.
        var heroMovie = movies
            .Where(m => m.IsHeroEligible && m.UpcomingShowtimesCount > 0)
            .OrderByDescending(m => !string.IsNullOrWhiteSpace(m.OfficialTrailerYoutubeId) ? 1 : 0)
            .ThenByDescending(m => m.UpcomingShowtimesCount)
            .FirstOrDefault()
            ?? movies.FirstOrDefault(m => m.IsHeroEligible);

        IReadOnlyList<MovieCastMemberDto> heroCast = [];
        IReadOnlyList<QuickPreviewShowtimeChipDto> heroNextShowtimes = [];
        if (heroMovie is not null)
        {
            var heroDetail = await catalogService.GetMovieDetailAsync(heroMovie.MovieId, ct);
            heroCast = heroDetail?.Cast ?? [];
            if (previewMap.TryGetValue(heroMovie.MovieId, out var heroPreview))
            {
                heroNextShowtimes = heroPreview.NextShowtimes;
            }
        }

        var onSaleMovies = movies
            .Where(m => m.UpcomingShowtimesCount > 0)
            .Select(m =>
            {
                var chips = previewMap.TryGetValue(m.MovieId, out var preview)
                    ? preview.NextShowtimes
                    : [];
                return new HomeOnSaleMovieItemViewModel(m, chips);
            })
            .ToList();

        var stringKeyMap = previewMap.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

        var vm = new HomeViewModel
        {
            HeroMovie = heroMovie,
            HeroCast = heroCast,
            HeroNextShowtimes = heroNextShowtimes,
            OnSaleMovies = onSaleMovies,
            FeaturedMovies = movies,
            UpcomingShowtimes = upcomingShowtimes,
            QuickPreviewJson = JsonSerializer.Serialize(stringKeyMap, PreviewJsonOptions)
        };

        return View(vm);
    }

    [Route("privacy")]
    public IActionResult Privacy() => View();

    [Route("access-denied")]
    public IActionResult AccessDenied() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Route("error")]
    public IActionResult Error(int? statusCode = null)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.HeaderKey]?.ToString()
            ?? HttpContext.TraceIdentifier;

        var vm = new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            CorrelationId = correlationId,
            StatusCode = statusCode ?? (Response.StatusCode != 200 ? Response.StatusCode : 500),
            Message = statusCode switch
            {
                404 => "Trang bạn đang tìm kiếm không tồn tại hoặc đã bị gỡ bỏ.",
                403 => "Bạn không có quyền truy cập vào tài nguyên này.",
                401 => "Vui lòng đăng nhập để tiếp tục.",
                _ => "Đã xảy ra sự cố kỹ thuật. Vui lòng thử lại sau."
            }
        };

        return View(vm);
    }
}
