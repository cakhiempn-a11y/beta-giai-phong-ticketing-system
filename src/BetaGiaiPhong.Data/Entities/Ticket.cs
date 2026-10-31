namespace BetaGiaiPhong.Data.Entities;

public sealed class Ticket
{
    public long TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public long ShowtimeId { get; set; }
    public long SeatId { get; set; }
    public string Status { get; set; } = string.Empty;
    public byte[] QrTokenHash { get; set; } = Array.Empty<byte>();
    public byte[] QrTokenCipher { get; set; } = Array.Empty<byte>();
    public string QrKeyId { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime? UsedUtc { get; set; }

    public OrderSeat? OrderSeat { get; set; }
    public ICollection<TicketScan> TicketScans { get; set; } = new List<TicketScan>();
}
