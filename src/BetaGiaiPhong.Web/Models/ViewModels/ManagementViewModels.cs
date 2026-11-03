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
    public bool IsTmdbConfigured { get; set; }
}

public sealed class ManageMovieFormViewModel
{
    public long? MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public short DurationMin { get; set; } = 120;
    public string? GenreText { get; set; }
    public string? AgeLabel { get; set; }
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string Status { get; set; } = "DRAFT";
    public bool IsEdit => MovieId.HasValue;
    public bool HasActiveShowtimes { get; set; }
    public bool IsTmdbConfigured { get; set; }

    public string? OriginalTitle { get; set; }
    public short? ReleaseYear { get; set; }
    public string? DirectorNames { get; set; }
    public string? OriginCountry { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? WikidataQid { get; set; }
    public string? ImdbId { get; set; }
    public int? TmdbId { get; set; }
    public string? BackdropUrl { get; set; }
    public string? OfficialTrailerYoutubeId { get; set; }
    public string? ArtworkSource { get; set; }
    public string? MetadataSource { get; set; }
    public string? RatingSource { get; set; }
    public decimal? RatingValue { get; set; }
    public int? RatingVoteCount { get; set; }
    public DateTime? ExternalSyncedUtc { get; set; }
    public string? ManualOverrideMask { get; set; }

    public bool HasOverride(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(ManualOverrideMask) || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var parts = ManualOverrideMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.Equals(part, fieldName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class ManageMovieDetailViewModel
{
    public Movie Movie { get; set; } = null!;
    public IReadOnlyList<Showtime> Showtimes { get; set; } = [];
    public bool IsTmdbConfigured { get; set; }
    public bool HasActiveShowtimes { get; set; }
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
