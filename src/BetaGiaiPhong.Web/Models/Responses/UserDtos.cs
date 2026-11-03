namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record UserProfileDto(
    long UserId,
    string Email,
    string? DisplayName,
    string Status,
    IReadOnlyList<string> Roles,
    string? CreatedVietnamText = null,
    int TotalOrdersCount = 0,
    int PaidOrdersCount = 0
);
