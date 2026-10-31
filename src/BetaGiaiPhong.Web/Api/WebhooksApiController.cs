using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/webhooks")]
[IgnoreAntiforgeryToken]
public sealed class WebhooksApiController(
    PaymentService paymentService,
    ILogger<WebhooksApiController> logger) : ControllerBase
{
    private const string HeaderSignatureSha256 = "X-Signature-SHA256";
    private const string HeaderSignatureAlt = "X-Signature";

    [HttpPost("mock-payment")]
    [RequestSizeLimit(32768)] // 32 KB limit
    [ProducesResponseType(typeof(PaymentWebhookResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleMockPaymentWebhook(
        [FromBody] MockPaymentWebhookRequest? request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "INVALID_PAYLOAD",
                Detail = "Nội dung webhook không được để trống."
            });
        }

        // 1. Extract signature from HTTP headers
        string? signature = null;
        if (Request.Headers.TryGetValue(HeaderSignatureSha256, out var sigValues) && sigValues.Count > 0)
        {
            signature = sigValues[0];
        }
        else if (Request.Headers.TryGetValue(HeaderSignatureAlt, out var altSigValues) && altSigValues.Count > 0)
        {
            signature = altSigValues[0];
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            logger.LogWarning("Mock payment webhook rejected: missing signature header.");
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "MISSING_SIGNATURE",
                Detail = "Thiếu tiêu đề chữ ký xác thực webhook (X-Signature-SHA256 hoặc X-Signature)."
            });
        }

        var message = new MockPaymentMessage(
            request.ProviderEventRef,
            request.AttemptKey,
            request.ProviderTransactionRef,
            request.OrderCode,
            request.AmountVnd,
            request.Outcome
        );

        // 2. Verify cryptographic HMAC-SHA256 signature
        VerifiedMockPaymentMessage verifiedMessage;
        try
        {
            verifiedMessage = MockPaymentSecurity.Verify(message, signature);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning("Mock payment webhook rejected: signature verification failed for OrderCode {OrderCode}.", request.OrderCode);
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "INVALID_SIGNATURE",
                Detail = ex.Message
            });
        }
        catch (BusinessRuleException ex)
        {
            logger.LogWarning("Mock payment webhook rejected: invalid payload fields for OrderCode {OrderCode}.", request.OrderCode);
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = ex.Code,
                Detail = ex.Message
            });
        }

        // 3. Process verified webhook in transactional domain service
        try
        {
            var result = await paymentService.ApplyVerifiedMockAsync(verifiedMessage, ct);

            logger.LogInformation(
                "Mock payment webhook processed successfully: OrderId {OrderId}, Decision {Decision}, Status {Status}",
                result.OrderId, result.Decision, result.OrderStatus);

            return Ok(new PaymentWebhookResponseDto(result.Decision, result.OrderId, result.OrderStatus));
        }
        catch (BusinessRuleException ex)
        {
            var status = ex.Code switch
            {
                "ORDER_NOT_FOUND" => StatusCodes.Status404NotFound,
                "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
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
}
