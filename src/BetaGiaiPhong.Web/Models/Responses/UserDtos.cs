namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record UserProfileDto(
    long UserId,
    string Email,
    string? DisplayName,
    string Status,
    IReadOnlyList<string> Roles);
