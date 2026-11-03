using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed record OrderTicketsViewModel(
    long OrderId,
    string OrderCode,
    string OrderStatus,
    string MovieTitle,
    string RoomName,
    DateTime StartUtc,
    string FormattedStartUtc,
    IReadOnlyList<CustomerTicketDto> Tickets,
    long ShowtimeId = 0,
    long MovieId = 0,
    string? MovieOriginalTitle = null,
    int? MovieReleaseYear = null,
    int MovieDurationMin = 0,
    string? MovieDirectorNames = null,
    string MovieAgeLabel = "P",
    string? MovieAgeLabelMarket = null,
    MovieRatingDto? MovieRating = null,
    MovieArtworkDto? MovieArtwork = null,
    string StartVietnamDateText = "",
    string StartVietnamTimeOnlyText = "",
    string EndVietnamTimeOnlyText = "",
    bool CrossesMidnight = false,
    string? PaidVietnamText = null,
    string? FlashMessage = null
);
