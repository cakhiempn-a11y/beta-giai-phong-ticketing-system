namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record ScanTicketResponse(
    string Outcome,
    bool IsSuccess,
    long? TicketId,
    string? TicketCode,
    string Message,
    DateTime ScannedUtc,
    string? MovieTitle = null,
    string? RoomName = null,
    string? SeatLabel = null,
    DateTime? ShowtimeStartUtc = null,
    DateTime? UsedUtc = null
);
