namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record MovieSummaryDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string Status,
    int UpcomingShowtimesCount,
    string? AgeLabel = null,
    string? Synopsis = null,
    string? PosterUrl = null);

public sealed record MovieDetailDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string Status,
    IReadOnlyList<ShowtimeDto> UpcomingShowtimes,
    string? AgeLabel = null,
    string? Synopsis = null,
    string? PosterUrl = null);
