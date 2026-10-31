using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class MovieDetailViewModel
{
    public MovieDetailDto Movie { get; set; } = null!;
    public IReadOnlyDictionary<string, List<ShowtimeDto>> ShowtimesByDate { get; set; } = new Dictionary<string, List<ShowtimeDto>>();
}
