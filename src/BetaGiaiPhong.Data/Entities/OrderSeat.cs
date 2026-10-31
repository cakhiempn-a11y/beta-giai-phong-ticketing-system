namespace BetaGiaiPhong.Data.Entities;

public sealed class OrderSeat
{
    public long OrderId { get; set; }
    public long ShowtimeId { get; set; }
    public long SeatId { get; set; }
    public string SeatTypeSnapshot { get; set; } = string.Empty;
    public long UnitPriceVnd { get; set; }

    public BookingOrder? Order { get; set; }
    public ShowtimeSeat? ShowtimeSeat { get; set; }
    public Ticket? Ticket { get; set; }
}
