using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class HomeViewModel
{
    public MovieSummaryDto? HeroMovie { get; set; }
    public IReadOnlyList<MovieSummaryDto> FeaturedMovies { get; set; } = [];
    public IReadOnlyList<ShowtimeDto> UpcomingShowtimes { get; set; } = [];
}
