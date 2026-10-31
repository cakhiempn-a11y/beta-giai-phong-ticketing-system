namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record ShowtimeDto(
    long ShowtimeId,
    long MovieId,
    string MovieTitle,
    long RoomId,
    string RoomName,
    DateTime StartUtc,
    DateTime EndUtc,
    string StartVietnamText,
    string EndVietnamText,
    short BufferMinutes,
    string Status,
    int? SellableCapacity,
    int AvailableSeatsCount);

public sealed record ShowtimeDetailDto(
    long ShowtimeId,
    long MovieId,
    string MovieTitle,
    short MovieDurationMin,
    string? MovieGenre,
    long RoomId,
    string RoomName,
    string RoomCode,
    DateTime StartUtc,
    DateTime EndUtc,
    string StartVietnamText,
    string EndVietnamText,
    string Status,
    int? SellableCapacity,
    int AvailableSeatsCount,
    IReadOnlyDictionary<string, long> PriceBySeatTypeVnd);
