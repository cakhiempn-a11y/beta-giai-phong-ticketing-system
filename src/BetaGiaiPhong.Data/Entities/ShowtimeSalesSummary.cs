namespace BetaGiaiPhong.Data.Entities;

public sealed class ShowtimeSalesSummary
{
    public long ShowtimeId { get; set; }
    public long MovieId { get; set; }
    public long RoomId { get; set; }
    public DateTime StartUtc { get; set; }
    public long TicketsSold { get; set; }
    public decimal RevenueVnd { get; set; }
    public int SellableCapacity { get; set; }
    public decimal OccupancyPct { get; set; }
}
