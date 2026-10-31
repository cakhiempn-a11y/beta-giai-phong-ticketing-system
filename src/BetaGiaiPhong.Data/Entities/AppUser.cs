namespace BetaGiaiPhong.Data.Entities;

public sealed class AppUser
{
    public long UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<BookingOrder> Orders { get; set; } = new List<BookingOrder>();
    public ICollection<TicketScan> ScansPerformed { get; set; } = new List<TicketScan>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
