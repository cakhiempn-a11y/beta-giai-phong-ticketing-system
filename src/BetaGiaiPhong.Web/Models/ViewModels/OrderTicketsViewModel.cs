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
    IReadOnlyList<CustomerTicketDto> Tickets
);
