using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed record CheckoutViewModel(
    long OrderId,
    string OrderCode,
    string MovieTitle,
    string RoomName,
    DateTime StartUtc,
    string FormattedStartUtc,
    IReadOnlyList<OrderSeatItemDto> Seats,
    long SubtotalVnd,
    long FeeVnd,
    long DiscountVnd,
    long TotalVnd,
    DateTime ExpiresUtc,
    int RemainingSeconds,
    bool IsExpired,
    string AttemptKey,
    bool IsSimulatorEnabled
);
