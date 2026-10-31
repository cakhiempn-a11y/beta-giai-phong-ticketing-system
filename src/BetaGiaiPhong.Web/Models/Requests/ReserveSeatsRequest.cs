using System.ComponentModel.DataAnnotations;

namespace BetaGiaiPhong.Web.Models.Requests;

public sealed class ReserveSeatsRequest
{
    [Required(ErrorMessage = "ShowtimeId là bắt buộc.")]
    [Range(1, long.MaxValue, ErrorMessage = "ShowtimeId không hợp lệ.")]
    public long ShowtimeId { get; set; }

    [Required(ErrorMessage = "Danh sách ghế là bắt buộc.")]
    public List<long> SeatIds { get; set; } = [];
}
