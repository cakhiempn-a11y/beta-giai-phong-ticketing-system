namespace BetaGiaiPhong.Data.Entities;

public sealed class SeatPrice
{
    public long ShowtimeId { get; set; }
    public string SeatTypeCode { get; set; } = string.Empty;
    public long AmountVnd { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public Showtime? Showtime { get; set; }
    public SeatType? SeatType { get; set; }
}
