namespace BetaGiaiPhong.Data.Entities;

public sealed class Payment
{
    public long PaymentId { get; set; }
    public long OrderId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string AttemptKey { get; set; } = string.Empty;
    public string? ProviderTransactionRef { get; set; }
    public long AmountVnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }

    public BookingOrder? Order { get; set; }
    public ICollection<PaymentEvent> PaymentEvents { get; set; } = new List<PaymentEvent>();
}
