using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Services;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class ManageMoviesViewModel
{
    public IReadOnlyList<MovieManagementSummaryDto> Items { get; set; } = [];
    public string? Search { get; set; }
    public string StatusFilter { get; set; } = "ALL";
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}

public sealed class ManageMovieFormViewModel
{
    public long? MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public short DurationMin { get; set; } = 120;
    public string? GenreText { get; set; }
    public string? AgeLabel { get; set; } = "T13";
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string Status { get; set; } = "DRAFT";
    public bool IsEdit => MovieId.HasValue;
    public bool HasActiveShowtimes { get; set; }
}

public sealed class ManageMovieDetailViewModel
{
    public Movie Movie { get; set; } = null!;
    public IReadOnlyList<Showtime> Showtimes { get; set; } = [];
}

public sealed class ManageShowtimesViewModel
{
    public IReadOnlyList<ShowtimeManagementSummaryDto> Items { get; set; } = [];
    public IReadOnlyList<Movie> Movies { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public long? SelectedMovieId { get; set; }
    public long? SelectedRoomId { get; set; }
    public string? DateFilter { get; set; }
    public string StatusFilter { get; set; } = "ALL";
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}

public sealed class ManageShowtimeCreateViewModel
{
    public IReadOnlyList<Movie> PublishedMovies { get; set; } = [];
    public IReadOnlyList<Room> ActiveRooms { get; set; } = [];
    public IReadOnlyList<SeatType> SeatTypes { get; set; } = [];
    public long? SelectedMovieId { get; set; }
    public long? SelectedRoomId { get; set; }
    public DateTime? StartTimeVn { get; set; }
    public short BufferMinutes { get; set; } = 15;
    public Dictionary<string, long> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ManageShowtimeDetailViewModel
{
    public ShowtimeManagementDetailDto Details { get; set; } = null!;
}

public sealed class ManageRoomsViewModel
{
    public IReadOnlyList<RoomManagementSummaryDto> Rooms { get; set; } = [];
}

public sealed class ManageRoomDetailViewModel
{
    public RoomManagementDetailDto Room { get; set; } = null!;
}

public sealed class ManageDashboardViewModel
{
    public string? FromDateVn { get; set; }
    public string? ToDateVn { get; set; }
    public long? SelectedMovieId { get; set; }
    public long? SelectedRoomId { get; set; }
    public IReadOnlyList<Movie> Movies { get; set; } = [];
    public IReadOnlyList<Room> Rooms { get; set; } = [];
    public SalesDashboardReportDto Report { get; set; } = null!;
}
