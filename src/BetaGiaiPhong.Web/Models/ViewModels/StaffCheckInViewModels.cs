namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed record StaffCheckInShowtimeViewModel(
    long ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    int SoldTickets,
    int? TotalCapacity
)
{
    public string FormattedTimeRange =>
        $"{StartUtc.AddHours(7):HH:mm} - {EndUtc.AddHours(7):HH:mm} ({StartUtc.AddHours(7):dd/MM/yyyy})";

    public string DisplayText =>
        $"{MovieTitle} | {RoomName} | {FormattedTimeRange}";
}

public sealed class StaffCheckInIndexViewModel
{
    public long? SelectedShowtimeId { get; set; }
    public IReadOnlyList<StaffCheckInShowtimeViewModel> Showtimes { get; set; } = [];
}

public sealed class StaffScanHistoryItemViewModel
{
    public long Id { get; set; }
    public DateTime ScannedUtc { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string? TicketCode { get; set; }
    public string? MovieTitle { get; set; }
    public string? RoomName { get; set; }
    public string? SeatLabel { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Details { get; set; }

    public string FormattedScannedTime => ScannedUtc.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss");
}

public sealed class StaffCheckInHistoryViewModel
{
    public IReadOnlyList<StaffScanHistoryItemViewModel> Items { get; set; } = [];
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; } = 0;
    public long? SelectedShowtimeId { get; set; }
    public string? SelectedOutcome { get; set; }
    public IReadOnlyList<StaffCheckInShowtimeViewModel> Showtimes { get; set; } = [];
}
