using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/movies")]
[Produces("application/json")]
public sealed class MoviesApiController(ICatalogService catalogService) : ControllerBase
{
    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<MovieSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MovieSummaryDto>>> GetMovies(CancellationToken ct)
    {
        var movies = await catalogService.GetPublishedMoviesAsync(ct);
        return Ok(movies);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(MovieDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDetailDto>> GetMovie(long id, CancellationToken ct)
    {
        var movie = await catalogService.GetMovieDetailAsync(id, ct);
        if (movie is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Movie Not Found",
                detail: $"Phim có mã ID {id} không tồn tại hoặc chưa được công bố.");
        }

        return Ok(movie);
    }

    [HttpGet("{id:long}/showtimes")]
    [ProducesResponseType(typeof(IReadOnlyList<ShowtimeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ShowtimeDto>>> GetMovieShowtimes(long id, CancellationToken ct)
    {
        var movie = await catalogService.GetMovieDetailAsync(id, ct);
        if (movie is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Movie Not Found",
                detail: $"Phim có mã ID {id} không tồn tại hoặc chưa được công bố.");
        }

        var showtimes = await catalogService.GetMovieShowtimesAsync(id, ct);
        return Ok(showtimes);
    }
}
