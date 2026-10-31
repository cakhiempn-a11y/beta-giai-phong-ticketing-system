namespace BetaGiaiPhong.Data.Entities;

public sealed class Showtime
{
    public long ShowtimeId { get; set; }
    public long MovieId { get; set; }
    public long RoomId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public short BufferMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? SellableCapacitySnapshot { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedUtc { get; set; }

    public Movie? Movie { get; set; }
    public Room? Room { get; set; }
    public AppUser? Creator { get; set; }
    public ICollection<SeatPrice> SeatPrices { get; set; } = new List<SeatPrice>();
    public ICollection<ShowtimeSeat> ShowtimeSeats { get; set; } = new List<ShowtimeSeat>();
    public ICollection<BookingOrder> Orders { get; set; } = new List<BookingOrder>();
}
