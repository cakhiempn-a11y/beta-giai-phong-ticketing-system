namespace BetaGiaiPhong.Data.Entities;

public sealed class ShowtimeSeat
{
    public long ShowtimeId { get; set; }
    public long SeatId { get; set; }
    public long RoomId { get; set; }
    public string State { get; set; } = string.Empty;
    public long? OwnerOrderId { get; set; }
    public DateTime? HoldExpiresUtc { get; set; }
    public byte[] RowVer { get; set; } = Array.Empty<byte>();

    public Showtime? Showtime { get; set; }
    public Seat? Seat { get; set; }
    public BookingOrder? OwnerOrder { get; set; }
    public ICollection<OrderSeat> OrderSeats { get; set; } = new List<OrderSeat>();
}
