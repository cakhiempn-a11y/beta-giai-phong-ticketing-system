using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/orders/{id:long}/tickets")]
[Authorize(Roles = "CUSTOMER")]
public sealed class TicketsApiController(
    ITicketQueryService ticketQueryService,
    BetaDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderTickets([FromRoute] long id, CancellationToken ct)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(rawUserId, out var customerId))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "UNAUTHORIZED",
                Detail = "Vui lòng đăng nhập để xem thông tin vé."
            });
        }

        // Verify order exists and belongs to current customer
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OrderId == id, ct);
        if (order is null || order.UserId != customerId)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "ORDER_NOT_FOUND",
                Detail = "Không tìm thấy đơn hàng yêu cầu."
            });
        }

        var tickets = await ticketQueryService.GetCustomerTicketDtosAsync(customerId, id, ct);
        return Ok(tickets);
    }
}
