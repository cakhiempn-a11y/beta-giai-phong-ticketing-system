namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record PaymentAttemptDto(
    long PaymentId,
    long OrderId,
    string OrderCode,
    string AttemptKey,
    long AmountVnd,
    string Provider,
    string Status,
    DateTime CreatedUtc
);

public sealed record PaymentWebhookResponseDto(
    string Decision,
    long OrderId,
    string OrderStatus
);
