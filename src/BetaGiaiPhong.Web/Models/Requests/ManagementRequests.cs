using System.ComponentModel.DataAnnotations;

namespace BetaGiaiPhong.Web.Models.Requests;

public sealed class CreateMovieRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên phim.")]
    [StringLength(250, ErrorMessage = "Tên phim không được vượt quá 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập thời lượng phim.")]
    [Range(1, 480, ErrorMessage = "Thời lượng phim phải từ 1 đến 480 phút.")]
    public short DurationMin { get; set; }

    [StringLength(160, ErrorMessage = "Thể loại phim không được vượt quá 160 ký tự.")]
    public string? GenreText { get; set; }

    [StringLength(24, ErrorMessage = "Nhãn độ tuổi không được vượt quá 24 ký tự.")]
    public string? AgeLabel { get; set; }

    public string? Synopsis { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn poster không được vượt quá 500 ký tự.")]
    public string? PosterUrl { get; set; }
}

public sealed class EditMovieRequest
{
    [Required]
    public long MovieId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên phim.")]
    [StringLength(250, ErrorMessage = "Tên phim không được vượt quá 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập thời lượng phim.")]
    [Range(1, 480, ErrorMessage = "Thời lượng phim phải từ 1 đến 480 phút.")]
    public short DurationMin { get; set; }

    [StringLength(160, ErrorMessage = "Thể loại phim không được vượt quá 160 ký tự.")]
    public string? GenreText { get; set; }

    [StringLength(24, ErrorMessage = "Nhãn độ tuổi không được vượt quá 24 ký tự.")]
    public string? AgeLabel { get; set; }

    public string? Synopsis { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn poster không được vượt quá 500 ký tự.")]
    public string? PosterUrl { get; set; }
}

public sealed class PublishShowtimeRequest
{
    [Required(ErrorMessage = "Vui lòng chọn phim.")]
    public long MovieId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn phòng chiếu.")]
    public long RoomId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thời gian bắt đầu chiếu (giờ Việt Nam).")]
    public DateTime StartTimeVn { get; set; }

    [Range(0, 120, ErrorMessage = "Thời gian đệm phải từ 0 đến 120 phút.")]
    public short BufferMinutes { get; set; } = 15;

    public Dictionary<string, long> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
