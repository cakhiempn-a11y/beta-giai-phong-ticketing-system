using System.ComponentModel.DataAnnotations;

namespace BetaGiaiPhong.Web.Models.Requests;

public sealed class ScanTicketRequest
{
    [Required(ErrorMessage = "Mã QR token không được để trống.")]
    [StringLength(256, ErrorMessage = "Mã QR token không được vượt quá 256 ký tự.")]
    public string QrToken { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "ID suất chiếu phải lớn hơn 0.")]
    public long ExpectedShowtimeId { get; set; }
}
