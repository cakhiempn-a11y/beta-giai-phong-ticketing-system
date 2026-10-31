namespace BetaGiaiPhong.Data.Entities;

public sealed class Seat
{
    public long SeatId { get; set; }
    public long RoomId { get; set; }
    public string SeatCode { get; set; } = string.Empty;
    public string SeatTypeCode { get; set; } = string.Empty;
    public string RowLabel { get; set; } = string.Empty;
    public short ColumnNo { get; set; }
    public bool IsActive { get; set; }

    public Room? Room { get; set; }
    public SeatType? SeatType { get; set; }
    public ICollection<ShowtimeSeat> ShowtimeSeats { get; set; } = new List<ShowtimeSeat>();
}
