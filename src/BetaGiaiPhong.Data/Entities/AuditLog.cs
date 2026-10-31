namespace BetaGiaiPhong.Data.Entities;

public sealed class AuditLog
{
    public long AuditId { get; set; }
    public long? ActorUserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityKey { get; set; }
    public Guid CorrelationId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime CreatedUtc { get; set; }

    public AppUser? ActorUser { get; set; }
}
