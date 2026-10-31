namespace BetaGiaiPhong.Data.Entities;

public sealed class SeatType
{
    public string SeatTypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
    public ICollection<SeatPrice> SeatPrices { get; set; } = new List<SeatPrice>();
}
