using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed record CatalogSelectOption(string Value, string Label);

public sealed class MovieCatalogViewModel
{
    public IReadOnlyList<MovieSummaryDto> Movies { get; init; } = [];
    public int TotalPublishedCount { get; init; }
    public int WithShowtimesCount { get; init; }
    public int WithoutShowtimesCount { get; init; }
    public int TotalFilteredCount { get; init; }

    public string SearchQuery { get; init; } = string.Empty;
    public string ActiveFilter { get; init; } = "all";
    public string SelectedGenre { get; init; } = string.Empty;
    public string SelectedAge { get; init; } = string.Empty;
    public int? SelectedYear { get; init; }
    public string SortBy { get; init; } = "default";

    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int PageSize { get; init; } = 24;

    public IReadOnlyList<string> AvailableGenres { get; init; } = [];
    public IReadOnlyList<CatalogSelectOption> AvailableAges { get; init; } = [];
    public IReadOnlyList<short> AvailableYears { get; init; } = [];

    public string QuickPreviewJson { get; init; } = "{}";

    public bool HasActiveFilterOrSearch =>
        !string.IsNullOrWhiteSpace(SearchQuery)
        || !string.Equals(ActiveFilter, "all", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(SelectedGenre)
        || !string.IsNullOrWhiteSpace(SelectedAge)
        || SelectedYear.HasValue
        || !string.Equals(SortBy, "default", StringComparison.OrdinalIgnoreCase);
}
