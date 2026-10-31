using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/orders/{id:long}/payments")]
[Authorize(Roles = "CUSTOMER")]
[AutoValidateAntiforgeryToken]
public sealed class PaymentsApiController(
    PaymentService paymentService,
    BetaDbContext db) : ControllerBase
{
    [HttpPost("mock-attempt")]
    [ProducesResponseType(typeof(PaymentAttemptDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMockAttempt(
        [FromRoute] long id,
        [FromBody] CreateMockAttemptRequest? request,
        CancellationToken ct)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(rawUserId, out var customerId))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "UNAUTHORIZED",
                Detail = "Vui lòng đăng nhập để thực hiện thanh toán."
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

        var attemptKey = string.IsNullOrWhiteSpace(request?.AttemptKey)
            ? "ATTEMPT-" + Guid.NewGuid().ToString("N")
            : request.AttemptKey.Trim();

        try
        {
            var paymentId = await paymentService.CreateMockAttemptAsync(customerId, id, attemptKey, ct);

            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId, ct);

            var dto = new PaymentAttemptDto(
                payment.PaymentId,
                payment.OrderId,
                order.OrderCode,
                payment.AttemptKey,
                payment.AmountVnd,
                payment.Provider,
                payment.Status,
                payment.CreatedUtc
            );

            return CreatedAtAction(nameof(GetPaymentAttempt), new { id, paymentId }, dto);
        }
        catch (BusinessRuleException ex)
        {
            var status = ex.Code switch
            {
                "ORDER_NOT_FOUND" => StatusCodes.Status404NotFound,
                "ORDER_UNAVAILABLE" => StatusCodes.Status409Conflict,
                "INVALID_ATTEMPT_KEY" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(status, new ProblemDetails
            {
                Status = status,
                Title = ex.Code,
                Detail = ex.Message
            });
        }
    }

    [HttpGet("{paymentId:long}")]
    [ProducesResponseType(typeof(PaymentAttemptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentAttempt(
        [FromRoute] long id,
        [FromRoute] long paymentId,
        CancellationToken ct)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(rawUserId, out var customerId) || customerId <= 0)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "UNAUTHORIZED",
                Detail = "Vui lòng đăng nhập để xem thông tin thanh toán."
            });
        }

        var payment = await db.Payments
            .AsNoTracking()
            .Include(p => p.Order)
            .SingleOrDefaultAsync(p => p.PaymentId == paymentId && p.OrderId == id, ct);

        if (payment is null || payment.Order is null || payment.Order.UserId != customerId)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "PAYMENT_NOT_FOUND",
                Detail = "Không tìm thấy thông tin giao dịch thanh toán yêu cầu."
            });
        }

        var dto = new PaymentAttemptDto(
            payment.PaymentId,
            payment.OrderId,
            payment.Order.OrderCode,
            payment.AttemptKey,
            payment.AmountVnd,
            payment.Provider,
            payment.Status,
            payment.CreatedUtc
        );

        return Ok(dto);
    }
}
