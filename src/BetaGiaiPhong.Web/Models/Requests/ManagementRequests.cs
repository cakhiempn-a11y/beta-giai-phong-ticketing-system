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
    [AllowedValues("P", "K", "T13", "T16", "T18", "C", null, "", ErrorMessage = "Nhãn độ tuổi không hợp lệ. Chỉ chấp nhận chuẩn Việt Nam: P, K, T13, T16, T18, C hoặc để trống khi chưa phân loại.")]
    public string? AgeLabel { get; set; }

    public string? Synopsis { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn poster không được vượt quá 500 ký tự.")]
    [RegularExpression(@"^(/(?!/)[^\\<>'""\s]*|https?://[^\s\\<>'""$]+)$", ErrorMessage = "Đường dẫn poster phải là URL HTTP/HTTPS hợp lệ hoặc đường dẫn tương đối an toàn bắt đầu bằng '/'.")]
    public string? PosterUrl { get; set; }

    [StringLength(250, ErrorMessage = "Tên gốc không được vượt quá 250 ký tự.")]
    public string? OriginalTitle { get; set; }

    [Range(1888, 2100, ErrorMessage = "Năm phát hành phải từ 1888 đến 2100.")]
    public short? ReleaseYear { get; set; }

    [StringLength(300, ErrorMessage = "Tên đạo diễn không được vượt quá 300 ký tự.")]
    public string? DirectorNames { get; set; }

    [StringLength(100, ErrorMessage = "Quốc gia sản xuất không được vượt quá 100 ký tự.")]
    public string? OriginCountry { get; set; }

    [StringLength(16, ErrorMessage = "Mã ngôn ngữ gốc không được vượt quá 16 ký tự.")]
    public string? OriginalLanguage { get; set; }

    [StringLength(32, ErrorMessage = "Mã Wikidata QID không hợp lệ.")]
    public string? WikidataQid { get; set; }

    [StringLength(24, ErrorMessage = "Mã IMDb ID không hợp lệ.")]
    public string? ImdbId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Mã TMDB ID phải là số nguyên dương.")]
    public int? TmdbId { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh nền (Backdrop) không được vượt quá 500 ký tự.")]
    [RegularExpression(@"^(/(?!/)[^\\<>'""\s]*|https?://[^\s\\<>'""$]+)$", ErrorMessage = "Đường dẫn ảnh nền phải là URL HTTP/HTTPS hợp lệ hoặc đường dẫn tương đối an toàn bắt đầu bằng '/'.")]
    public string? BackdropUrl { get; set; }

    [StringLength(32, ErrorMessage = "Mã YouTube Trailer ID không được vượt quá 32 ký tự.")]
    public string? OfficialTrailerYoutubeId { get; set; }

    public List<string>? ManualOverrides { get; set; }
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
    [AllowedValues("P", "K", "T13", "T16", "T18", "C", null, "", ErrorMessage = "Nhãn độ tuổi không hợp lệ. Chỉ chấp nhận chuẩn Việt Nam: P, K, T13, T16, T18, C hoặc để trống khi chưa phân loại.")]
    public string? AgeLabel { get; set; }

    public string? Synopsis { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn poster không được vượt quá 500 ký tự.")]
    [RegularExpression(@"^(/(?!/)[^\\<>'""\s]*|https?://[^\s\\<>'""$]+)$", ErrorMessage = "Đường dẫn poster phải là URL HTTP/HTTPS hợp lệ hoặc đường dẫn tương đối an toàn bắt đầu bằng '/'.")]
    public string? PosterUrl { get; set; }

    [StringLength(250, ErrorMessage = "Tên gốc không được vượt quá 250 ký tự.")]
    public string? OriginalTitle { get; set; }

    [Range(1888, 2100, ErrorMessage = "Năm phát hành phải từ 1888 đến 2100.")]
    public short? ReleaseYear { get; set; }

    [StringLength(300, ErrorMessage = "Tên đạo diễn không được vượt quá 300 ký tự.")]
    public string? DirectorNames { get; set; }

    [StringLength(100, ErrorMessage = "Quốc gia sản xuất không được vượt quá 100 ký tự.")]
    public string? OriginCountry { get; set; }

    [StringLength(16, ErrorMessage = "Mã ngôn ngữ gốc không được vượt quá 16 ký tự.")]
    public string? OriginalLanguage { get; set; }

    [StringLength(32, ErrorMessage = "Mã Wikidata QID không hợp lệ.")]
    public string? WikidataQid { get; set; }

    [StringLength(24, ErrorMessage = "Mã IMDb ID không hợp lệ.")]
    public string? ImdbId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Mã TMDB ID phải là số nguyên dương.")]
    public int? TmdbId { get; set; }

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh nền (Backdrop) không được vượt quá 500 ký tự.")]
    [RegularExpression(@"^(/(?!/)[^\\<>'""\s]*|https?://[^\s\\<>'""$]+)$", ErrorMessage = "Đường dẫn ảnh nền phải là URL HTTP/HTTPS hợp lệ hoặc đường dẫn tương đối an toàn bắt đầu bằng '/'.")]
    public string? BackdropUrl { get; set; }

    [StringLength(32, ErrorMessage = "Mã YouTube Trailer ID không được vượt quá 32 ký tự.")]
    public string? OfficialTrailerYoutubeId { get; set; }

    public bool ApplyOverrideLocks { get; set; }

    public List<string>? ManualOverrides { get; set; }
}

public sealed class SyncExternalMovieRequest
{
    [StringLength(64)]
    public string? VerifiedKey { get; set; }

    public int? TmdbId { get; set; }

    public long? TargetMovieId { get; set; }
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
