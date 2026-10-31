namespace BetaGiaiPhong.Web.Models.Requests;

public sealed record CreateMockAttemptRequest(
    string? AttemptKey = null
);

public sealed record MockPaymentWebhookRequest(
    string ProviderEventRef,
    string AttemptKey,
    string ProviderTransactionRef,
    string OrderCode,
    long AmountVnd,
    string Outcome
);

public sealed record DemoPaymentRequest(
    string? Outcome = "SUCCESS"
);
