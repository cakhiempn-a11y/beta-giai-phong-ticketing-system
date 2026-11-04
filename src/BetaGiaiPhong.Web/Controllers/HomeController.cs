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

        // Deterministic Grounded Billboard Carousel Selection (up to 6 Hero-eligible works for 16:9 Thumbnail Strip):
        // 1. Prefer Hero-eligible published movies (verified BackdropUrl + PosterUrl + Synopsis) with upcoming showtimes,
        //    ranking movies with an official trailer and rich multi-slot schedule first.
        // 2. Fallback to remaining Hero-eligible published movies.
        // 3. Movies lacking real backdrop/poster/synopsis (e.g. PENDING_TMDB_API_KEY) are strictly excluded from Billboard.
        const int MaxBillboardSlides = 6;

        var billboardMovies = movies
            .Where(m => m.IsHeroEligible)
            .OrderByDescending(m => m.UpcomingShowtimesCount > 0 ? 1 : 0)
            .ThenByDescending(m => !string.IsNullOrWhiteSpace(m.OfficialTrailerYoutubeId) ? 1 : 0)
            .ThenByDescending(m => m.ReleaseYear ?? 0)
            .ThenByDescending(m => m.UpcomingShowtimesCount)
            .ThenByDescending(m => m.Rating?.Score ?? 0m)
            .Take(MaxBillboardSlides)
            .ToList();

        var castMap = await catalogService.GetMoviesCastMapAsync(billboardMovies.Select(m => m.MovieId), ct);

        var billboardSlides = billboardMovies
            .Select(m =>
            {
                var cast = castMap.TryGetValue(m.MovieId, out var c) ? c : [];
                var nextSlots = previewMap.TryGetValue(m.MovieId, out var p) ? p.NextShowtimes : [];
                return new HomeBillboardSlideViewModel(m, cast, nextSlots);
            })
            .ToList();

        var primarySlide = billboardSlides.FirstOrDefault();

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

        var acclaimedMovies = movies
            .Where(m => m.Artwork?.HasVerifiedPoster == true && m.Rating != null)
            .OrderByDescending(m => m.Rating!.Score)
            .ThenByDescending(m => m.UpcomingShowtimesCount)
            .Take(8)
            .ToList();

        var stringKeyMap = previewMap.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

        var vm = new HomeViewModel
        {
            HeroMovie = primarySlide?.Movie,
            HeroCast = primarySlide?.Cast ?? [],
            HeroNextShowtimes = primarySlide?.NextShowtimes ?? [],
            BillboardSlides = billboardSlides,
            OnSaleMovies = onSaleMovies,
            AcclaimedMovies = acclaimedMovies,
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
