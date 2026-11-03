using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class MovieCatalogViewModel
{
    public IReadOnlyList<MovieSummaryDto> Movies { get; init; } = [];
    public int TotalPublishedCount { get; init; }
    public int WithShowtimesCount { get; init; }
    public int WithoutShowtimesCount { get; init; }
    public string SearchQuery { get; init; } = string.Empty;
    public string ActiveFilter { get; init; } = "all";
    public IReadOnlyList<string> AvailableGenres { get; init; } = [];
    public string QuickPreviewJson { get; init; } = "{}";
    public bool HasActiveFilterOrSearch => !string.IsNullOrWhiteSpace(SearchQuery) || !string.Equals(ActiveFilter, "all", StringComparison.OrdinalIgnoreCase);
}
