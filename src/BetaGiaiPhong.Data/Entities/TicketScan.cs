namespace BetaGiaiPhong.Data.Entities;

public sealed class TicketScan
{
    public long ScanId { get; set; }
    public long TicketId { get; set; }
    public long StaffUserId { get; set; }
    public DateTime ScannedUtc { get; set; }
    public string Outcome { get; set; } = string.Empty;

    public Ticket? Ticket { get; set; }
    public AppUser? StaffUser { get; set; }
}
