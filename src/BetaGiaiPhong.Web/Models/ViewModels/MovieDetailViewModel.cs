using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class ShowtimeItemViewModel
{
    public required ShowtimeDto Showtime { get; init; }
    public required DateTimeOffset StartVietnam { get; init; }
    public required DateTimeOffset EndVietnam { get; init; }
    public string StartTimeText => StartVietnam.ToString("HH:mm");
    public string EndTimeText => EndVietnam.ToString("HH:mm");
    public bool CrossesMidnight => EndVietnam.Date > StartVietnam.Date;
    public string EndDateShortText => EndVietnam.ToString("dd/MM");
    public bool IsSoldOut => Showtime.AvailableSeatsCount <= 0;
    public bool IsLowAvailability => Showtime.AvailableSeatsCount > 0 && Showtime.AvailableSeatsCount <= 10;
}

public sealed class ShowtimeDateGroupViewModel
{
    public required DateOnly DateVietnam { get; init; }
    public string DateKey => DateVietnam.ToString("yyyy-MM-dd");
    public string DateDisplayText => DateVietnam.ToString("dd/MM/yyyy");
    public string DateShortText => DateVietnam.ToString("dd/MM");
    public required string DayOfWeekLabel { get; init; }
    public required IReadOnlyList<ShowtimeItemViewModel> Showtimes { get; init; }
}

public sealed class MovieDetailViewModel
{
    public MovieDetailDto Movie { get; set; } = null!;
    public IReadOnlyDictionary<string, List<ShowtimeDto>> ShowtimesByDate { get; set; } = new Dictionary<string, List<ShowtimeDto>>();
    public IReadOnlyList<ShowtimeDateGroupViewModel> DateGroups { get; set; } = [];
    public string SelectedDateKey { get; set; } = string.Empty;
    public IReadOnlyList<MovieSummaryDto> SuggestedOnSaleMovies { get; set; } = [];
}
