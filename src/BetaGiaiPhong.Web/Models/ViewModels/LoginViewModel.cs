using BetaGiaiPhong.Web.Models.Requests;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class LoginViewModel
{
    public LoginRequest Request { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
