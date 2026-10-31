using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = "RequireCustomer")]
public sealed class OrdersApiController(
    IBookingQueryService bookingQueryService,
    OrderLifecycleService orderLifecycleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
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

        var (orders, totalCount) = await bookingQueryService.GetCustomerOrdersAsync(customerId, page, pageSize, ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        if (totalPages == 0) totalPages = 1;

        return Ok(new
        {
            orders,
            totalCount,
            page,
            pageSize,
            totalPages
        });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetOrderDetail(long id, CancellationToken ct = default)
    {
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

        var order = await bookingQueryService.GetCustomerOrderDetailAsync(customerId, id, ct);
        if (order is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource Not Found",
                Detail = "Không tìm thấy đơn hàng hoặc đơn hàng không thuộc về bạn.",
                Extensions = { ["errorCode"] = "ORDER_NOT_FOUND" }
            });
        }

        return Ok(order);
    }

    [HttpPost("{id:long}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(long id, CancellationToken ct = default)
    {
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

        var success = await orderLifecycleService.CancelAsync(customerId, id, ct);
        if (!success)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource Not Found",
                Detail = "Không tìm thấy đơn hàng hoặc không thể hủy.",
                Extensions = { ["errorCode"] = "ORDER_NOT_FOUND" }
            });
        }

        return Ok(new CancelOrderResultDto(id, OrderStates.Cancelled, "Hủy đơn hàng thành công."));
    }
}
