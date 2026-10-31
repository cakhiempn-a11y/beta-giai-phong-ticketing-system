using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/showtimes")]
[Produces("application/json")]
public sealed class ShowtimesApiController(ICatalogService catalogService) : ControllerBase
{
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ShowtimeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShowtimeDetailDto>> GetShowtime(long id, CancellationToken ct)
    {
        var showtime = await catalogService.GetShowtimeDetailAsync(id, ct);
        if (showtime is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Showtime Not Found",
                detail: $"Suất chiếu có mã ID {id} không tồn tại hoặc chưa được mở bán.");
        }

        return Ok(showtime);
    }

    [HttpGet("{id:long}/seats")]
    [ProducesResponseType(typeof(SeatMapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeatMapDto>> GetSeatMap(long id, CancellationToken ct)
    {
        var seatMap = await catalogService.GetShowtimeSeatMapAsync(id, ct);
        if (seatMap is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Showtime Not Found",
                detail: $"Suất chiếu có mã ID {id} không tồn tại hoặc chưa sẵn sàng.");
        }

        return Ok(seatMap);
    }
}
