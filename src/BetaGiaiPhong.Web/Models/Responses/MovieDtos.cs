namespace BetaGiaiPhong.Web.Models.Responses;

public sealed record MovieRatingDto(
    string Source,
    decimal Score,
    decimal ScaleMax,
    int VoteCount,
    DateTime? FetchedUtc,
    string DisplayText);

public sealed record MovieArtworkDto(
    string? PosterUrl,
    string? PosterW342Url,
    string? BackdropUrl,
    string? BackdropW780Url,
    string? TitleLogoUrl,
    string Source,
    string? LicenseText,
    string? AttributionText,
    bool HasVerifiedPoster,
    bool HasVerifiedBackdrop);

public sealed record MovieCastMemberDto(
    string Name,
    string? Character);

public sealed record MovieProvenanceDto(
    int? TmdbId,
    string? ImdbId,
    string? WikidataQid,
    string? MetadataSource,
    string? AgeLabelMarket,
    string? AgeLabelSource,
    DateTime? ExternalSyncedUtc,
    DateTime? ExternalExpiresUtc);

public sealed record QuickPreviewShowtimeChipDto(
    long ShowtimeId,
    string StartVietnamText,
    string StartTimeOnlyText,
    string RoomName,
    int AvailableSeatsCount,
    bool CrossesMidnight = false);

public sealed record MovieQuickPreviewDto(
    long MovieId,
    string Title,
    string? OriginalTitle,
    short? ReleaseYear,
    short DurationMin,
    string? GenreText,
    string? AgeLabel,
    string? AgeLabelMarket,
    string? DirectorNames,
    string? ShortSynopsis,
    string? OfficialTrailerYoutubeId,
    int UpcomingShowtimesCount,
    IReadOnlyList<QuickPreviewShowtimeChipDto> NextShowtimes,
    MovieRatingDto? Rating,
    MovieArtworkDto Artwork);

public sealed record MovieSummaryDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string Status,
    int UpcomingShowtimesCount,
    string? AgeLabel = null,
    string? Synopsis = null,
    string? PosterUrl = null,
    string? OriginalTitle = null,
    short? ReleaseYear = null,
    DateOnly? ReleaseDateVn = null,
    string? OriginCountry = null,
    string? OriginalLanguage = null,
    string? DirectorNames = null,
    string? OfficialTrailerYoutubeId = null,
    bool IsHeroEligible = false,
    MovieRatingDto? Rating = null,
    MovieArtworkDto? Artwork = null,
    MovieProvenanceDto? Provenance = null);

public sealed record MovieDetailDto(
    long MovieId,
    string Title,
    short DurationMin,
    string? GenreText,
    string Status,
    IReadOnlyList<ShowtimeDto> UpcomingShowtimes,
    string? AgeLabel = null,
    string? Synopsis = null,
    string? PosterUrl = null,
    string? OriginalTitle = null,
    short? ReleaseYear = null,
    DateOnly? ReleaseDateVn = null,
    string? OriginCountry = null,
    string? OriginalLanguage = null,
    string? DirectorNames = null,
    string? OfficialTrailerYoutubeId = null,
    bool IsHeroEligible = false,
    MovieRatingDto? Rating = null,
    MovieArtworkDto? Artwork = null,
    MovieProvenanceDto? Provenance = null,
    IReadOnlyList<MovieCastMemberDto>? Cast = null);
