using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class OrderListViewModel
{
    public IReadOnlyList<OrderSummaryDto> Orders { get; set; } = [];
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public string? StatusFilter { get; set; }
    public string? FlashMessage { get; set; }
    public string? FlashError { get; set; }
}
