namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record SeatDto(
    long SeatId,
    string SeatCode,
    string RowLabel,
    short ColumnNo,
    string SeatTypeCode,
    string SeatTypeName,
    long PriceVnd,
    string DisplayState,
    bool IsBookable);

public sealed record SeatMapDto(
    long ShowtimeId,
    long MovieId,
    string MovieTitle,
    long RoomId,
    string RoomName,
    DateTime StartUtc,
    string StartVietnamText,
    int TotalCapacity,
    int AvailableCount,
    int SoldCount,
    int HeldCount,
    IReadOnlyDictionary<string, long> PriceTiersVnd,
    IReadOnlyList<SeatDto> Seats,
    bool IsOpenForBooking = true,
    string? IneligibleReason = null);
