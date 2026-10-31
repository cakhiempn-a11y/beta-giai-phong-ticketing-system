using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/bookings")]
[Authorize(Policy = "RequireCustomer")]
public sealed class BookingsApiController(SeatReservationService reservationService) : ControllerBase
{
    [HttpPost("reserve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reserve([FromBody] ReserveSeatsRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = errors,
                Extensions = { ["errorCode"] = "INVALID_SELECTION" }
            });
        }

        if (request.SeatIds == null || request.SeatIds.Count is < 1 or > 8 ||
            request.SeatIds.Any(id => id <= 0) || request.SeatIds.Distinct().Count() != request.SeatIds.Count)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "Vui lòng chọn từ 1 đến 8 ghế hợp lệ và không trùng lặp.",
                Extensions = { ["errorCode"] = "INVALID_SELECTION" }
            });
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdStr, out var customerId))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Phiên làm việc không hợp lệ.",
                Extensions = { ["errorCode"] = "UNAUTHORIZED" }
            });
        }

        var result = await reservationService.ReserveAsync(customerId, request.ShowtimeId, request.SeatIds, ct);

        var response = new ReservationResponseDto(
            result.OrderId,
            result.OrderCode,
            request.ShowtimeId,
            request.SeatIds,
            result.TotalVnd,
            result.ExpiresUtc,
            OrderStates.Pending
        );

        return Created($"/api/orders/{result.OrderId}", response);
    }
}
