using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("movies")]
public sealed class MoviesController(ICatalogService catalogService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var movies = await catalogService.GetPublishedMoviesAsync(ct);
        return View(movies);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var movie = await catalogService.GetMovieDetailAsync(id, ct);
        if (movie is null)
        {
            return NotFound();
        }

        // Group showtimes by Vietnam date (e.g., "31/10/2026")
        var groupedShowtimes = movie.UpcomingShowtimes
            .GroupBy(s => s.StartVietnamText.Split(' ')[0])
            .ToDictionary(g => g.Key, g => g.ToList());

        var vm = new MovieDetailViewModel
        {
            Movie = movie,
            ShowtimesByDate = groupedShowtimes
        };

        return View(vm);
    }
}
