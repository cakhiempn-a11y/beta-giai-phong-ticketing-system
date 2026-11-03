using System.ComponentModel.DataAnnotations.Schema;

namespace BetaGiaiPhong.Data.Entities;

public sealed class Movie
{
    public long MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public short DurationMin { get; set; }
    public string? GenreText { get; set; }
    public string? AgeLabel { get; set; }
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string Status { get; set; } = string.Empty;

    public string? OriginalTitle { get; set; }
    public short? ReleaseYear { get; set; }
    public DateOnly? ReleaseDateVn { get; set; }
    public string? OriginCountry { get; set; }
    public string? OriginalLanguage { get; set; }
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? WikidataQid { get; set; }

    public string? PosterW342Url { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropW780Url { get; set; }
    public string? TitleLogoUrl { get; set; }
    public string? OfficialTrailerYoutubeId { get; set; }
    public string? ArtworkSource { get; set; }
    public string? ArtworkLicenseText { get; set; }
    public string? ArtworkAttributionText { get; set; }

    public string? RatingSource { get; set; }
    public decimal? RatingValue { get; set; }
    public decimal? RatingScaleMax { get; set; }
    public int? RatingVoteCount { get; set; }
    public DateTime? RatingFetchedUtc { get; set; }

    public string? AgeLabelMarket { get; set; }
    public string? AgeLabelSource { get; set; }
    public string? DirectorNames { get; set; }
    public string? CastJson { get; set; }
    public string? MetadataSource { get; set; }
    public DateTime? ExternalSyncedUtc { get; set; }
    public DateTime? ExternalExpiresUtc { get; set; }
    public string? ManualOverrideMask { get; set; }

    public ICollection<Showtime> Showtimes { get; set; } = new List<Showtime>();

    [NotMapped]
    public bool IsHeroEligible =>
        string.Equals(Status, "PUBLISHED", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(BackdropUrl) &&
        !string.IsNullOrWhiteSpace(PosterUrl) &&
        !PosterUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(ArtworkSource, "NONE", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(ArtworkSource, "PENDING_TMDB_API_KEY", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Synopsis);

    public bool HasManualOverride(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(ManualOverrideMask) || string.IsNullOrWhiteSpace(fieldName))
            return false;

        var parts = ManualOverrideMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.Equals(part, fieldName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public void AddManualOverrides(params string[] fieldNames)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(ManualOverrideMask))
        {
            var existing = ManualOverrideMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var item in existing)
            {
                set.Add(item.ToLowerInvariant());
            }
        }

        foreach (var field in fieldNames)
        {
            if (!string.IsNullOrWhiteSpace(field))
            {
                set.Add(field.Trim().ToLowerInvariant());
            }
        }

        ManualOverrideMask = set.Count == 0 ? null : string.Join(",", set);
    }

    public void SetManualOverrides(IEnumerable<string>? fieldNames)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fieldNames is not null)
        {
            foreach (var field in fieldNames)
            {
                if (!string.IsNullOrWhiteSpace(field))
                {
                    set.Add(field.Trim().ToLowerInvariant());
                }
            }
        }

        ManualOverrideMask = set.Count == 0 ? null : string.Join(",", set);
    }
}
