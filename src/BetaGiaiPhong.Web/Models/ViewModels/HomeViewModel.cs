using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed record HomeOnSaleMovieItemViewModel(
    MovieSummaryDto Movie,
    IReadOnlyList<QuickPreviewShowtimeChipDto> NextShowtimes);

public sealed record HomeBillboardSlideViewModel(
    MovieSummaryDto Movie,
    IReadOnlyList<MovieCastMemberDto> Cast,
    IReadOnlyList<QuickPreviewShowtimeChipDto> NextShowtimes,
    string? DirectVideoPreviewUrl = null);

public sealed class HomeViewModel
{
    public MovieSummaryDto? HeroMovie { get; set; }
    public IReadOnlyList<MovieCastMemberDto> HeroCast { get; set; } = [];
    public IReadOnlyList<QuickPreviewShowtimeChipDto> HeroNextShowtimes { get; set; } = [];
    public IReadOnlyList<HomeBillboardSlideViewModel> BillboardSlides { get; set; } = [];
    public IReadOnlyList<HomeOnSaleMovieItemViewModel> OnSaleMovies { get; set; } = [];
    public IReadOnlyList<MovieSummaryDto> AcclaimedMovies { get; set; } = [];
    public IReadOnlyList<MovieSummaryDto> FeaturedMovies { get; set; } = [];
    public IReadOnlyList<ShowtimeDto> UpcomingShowtimes { get; set; } = [];
    public string QuickPreviewJson { get; set; } = "{}";
}
