using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Middleware;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

public sealed class HomeController(ICatalogService catalogService) : Controller
{
    [HttpGet("")]
    [HttpGet("home")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var movies = await catalogService.GetPublishedMoviesAsync(ct);
        var upcomingShowtimes = await catalogService.GetUpcomingShowtimesAsync(8, ct);

        // Deterministic Hero Movie Selection:
        // 1. Pick the published movie that has bookable upcoming showtimes (UpcomingShowtimesCount > 0).
        // 2. If none have upcoming showtimes, pick the first published movie.
        // 3. If no published movies exist, HeroMovie is null (triggers cinema intro hero).
        var heroMovie = movies.FirstOrDefault(m => m.UpcomingShowtimesCount > 0)
            ?? movies.FirstOrDefault();

        var vm = new HomeViewModel
        {
            HeroMovie = heroMovie,
            FeaturedMovies = movies,
            UpcomingShowtimes = upcomingShowtimes
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
