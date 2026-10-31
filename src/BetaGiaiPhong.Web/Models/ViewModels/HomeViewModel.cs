using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class HomeViewModel
{
    public IReadOnlyList<MovieSummaryDto> FeaturedMovies { get; set; } = [];
    public IReadOnlyList<ShowtimeDto> UpcomingShowtimes { get; set; } = [];
}
