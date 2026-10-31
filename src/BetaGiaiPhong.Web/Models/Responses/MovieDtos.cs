namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record MovieSummaryDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string Status,
    int UpcomingShowtimesCount);

public sealed record MovieDetailDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string Status,
    IReadOnlyList<ShowtimeDto> UpcomingShowtimes);
