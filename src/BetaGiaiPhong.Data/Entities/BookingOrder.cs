namespace BetaGiaiPhong.Data.Entities;

public sealed class BookingOrder
{
    public long OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public long UserId { get; set; }
    public long ShowtimeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public byte SeatCount { get; set; }
    public long SubtotalVnd { get; set; }
    public long FeeVnd { get; set; }
    public long DiscountVnd { get; set; }
    public long TotalVnd { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? PaidUtc { get; set; }
    public byte[] RowVer { get; set; } = Array.Empty<byte>();

    public AppUser? User { get; set; }
    public Showtime? Showtime { get; set; }
    public ICollection<OrderSeat> OrderSeats { get; set; } = new List<OrderSeat>();
    public ICollection<ShowtimeSeat> OwnedShowtimeSeats { get; set; } = new List<ShowtimeSeat>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
