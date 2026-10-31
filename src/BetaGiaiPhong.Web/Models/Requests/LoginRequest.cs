using System.ComponentModel.DataAnnotations;

namespace BetaGiaiPhong.Web.Models.Requests;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ.")]
    [StringLength(256, ErrorMessage = "Email không được vượt quá 256 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 ký tự trở lên.")]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
