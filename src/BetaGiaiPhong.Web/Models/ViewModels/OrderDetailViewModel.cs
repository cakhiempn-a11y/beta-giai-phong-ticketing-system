using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class OrderDetailViewModel
{
    public OrderDetailDto Order { get; set; } = default!;
    public string? FlashMessage { get; set; }
    public string? FlashWarning { get; set; }
    public string? FlashError { get; set; }
}
