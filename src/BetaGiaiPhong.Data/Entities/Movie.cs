namespace BetaGiaiPhong.Data.Entities;

public sealed class Movie
{
    public long MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public short DurationMin { get; set; }
    public string? GenreText { get; set; }
    public string? AgeLabel { get; set; }
    public string? Synopsis { get; set; }
    public string? PosterUrl { get; set; }
    public string Status { get; set; } = string.Empty;

    public ICollection<Showtime> Showtimes { get; set; } = new List<Showtime>();
}
