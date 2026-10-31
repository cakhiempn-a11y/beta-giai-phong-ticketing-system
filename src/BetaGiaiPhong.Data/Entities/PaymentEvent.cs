namespace BetaGiaiPhong.Data.Entities;

public sealed class PaymentEvent
{
    public long PaymentEventId { get; set; }
    public long? PaymentId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderEventRef { get; set; }
    public string? ProviderTransactionRef { get; set; }
    public string AuthResult { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public byte[]? PayloadSha256 { get; set; }
    public DateTime ReceivedUtc { get; set; }

    public Payment? Payment { get; set; }
}
