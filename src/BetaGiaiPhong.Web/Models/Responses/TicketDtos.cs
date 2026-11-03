namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record CustomerTicketDto(
    long TicketId,
    string TicketCode,
    long OrderId,
    string OrderCode,
    long ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime StartUtc,
    string FormattedStartUtc,
    long SeatId,
    string SeatCode,
    string SeatType,
    long PriceVnd,
    string Status,
    DateTime IssuedUtc,
    DateTime? UsedUtc,
    string QrCodeUrl,
    string IssuedVietnamText = "",
    string? UsedVietnamText = null
);
