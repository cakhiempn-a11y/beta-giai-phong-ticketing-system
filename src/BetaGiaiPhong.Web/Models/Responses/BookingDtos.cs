namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record ReservationResponseDto(
    long OrderId,
    string OrderCode,
    long ShowtimeId,
    IReadOnlyList<long> SeatIds,
    long TotalVnd,
    DateTime ExpiresUtc,
    string Status
);

public sealed record OrderSummaryDto(
    long OrderId,
    string OrderCode,
    long ShowtimeId,
    string MovieTitle,
    DateTime StartUtc,
    string StartVietnamText,
    byte SeatCount,
    long TotalVnd,
    string Status,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    bool IsHoldExpired,
    bool CanCancel,
    string CreatedVietnamText = "",
    string RoomName = "",
    string SeatCodesSummary = "",
    long MovieId = 0,
    string? MovieOriginalTitle = null,
    int? MovieReleaseYear = null,
    string MovieAgeLabel = "P",
    string? MovieAgeLabelMarket = null,
    MovieArtworkDto? MovieArtwork = null
);

public sealed record OrderSeatItemDto(
    long SeatId,
    string SeatCode,
    string SeatTypeSnapshot,
    long UnitPriceVnd
);

public sealed record OrderDetailDto(
    long OrderId,
    string OrderCode,
    string OrderStatus,
    long ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime StartUtc,
    string StartVietnamText,
    IReadOnlyList<OrderSeatItemDto> Seats,
    long SubtotalVnd,
    long FeeVnd,
    long DiscountVnd,
    long TotalVnd,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    int RemainingSeconds,
    bool IsHoldExpired,
    bool CanCancel,
    string CreatedVietnamText = "",
    string ExpiresVietnamText = "",
    string ExpiresVietnamTimeOnlyText = "",
    DateTime? PaidUtc = null,
    string? PaidVietnamText = null,
    DateTime? EndUtc = null,
    string StartVietnamDateText = "",
    string StartVietnamTimeOnlyText = "",
    string EndVietnamTimeOnlyText = "",
    bool CrossesMidnight = false,
    long MovieId = 0,
    string? MovieOriginalTitle = null,
    int? MovieReleaseYear = null,
    int MovieDurationMin = 0,
    string? MovieDirectorNames = null,
    string? MovieGenreText = null,
    string MovieAgeLabel = "P",
    string? MovieAgeLabelMarket = null,
    string? PosterUrl = null,
    MovieRatingDto? MovieRating = null,
    MovieArtworkDto? MovieArtwork = null
);

public sealed record CancelOrderResultDto(
    long OrderId,
    string Status,
    string Message
);
