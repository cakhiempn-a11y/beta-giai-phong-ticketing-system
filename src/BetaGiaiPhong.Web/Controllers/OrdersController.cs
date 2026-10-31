using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("orders")]
[Authorize(Policy = "RequireCustomer")]
public sealed class OrdersController(
    IBookingQueryService bookingQueryService,
    OrderLifecycleService orderLifecycleService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int page = 1, CancellationToken ct = default)
    {
        var customerId = GetCurrentCustomerId();
        var (orders, totalCount) = await bookingQueryService.GetCustomerOrdersAsync(customerId, page, 10, ct);

        var viewModel = new OrderListViewModel
        {
            Orders = orders,
            CurrentPage = page,
            PageSize = 10,
            TotalCount = totalCount,
            TotalPages = (int)Math.Max(1, Math.Ceiling((double)totalCount / 10)),
            FlashMessage = TempData["Success"]?.ToString(),
            FlashError = TempData["Error"]?.ToString()
        };

        return View(viewModel);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken ct = default)
    {
        var customerId = GetCurrentCustomerId();
        var order = await bookingQueryService.GetCustomerOrderDetailAsync(customerId, id, ct);

        if (order is null)
        {
            TempData["Error"] = "Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn.";
            return RedirectToAction(nameof(Index));
        }

        var viewModel = new OrderDetailViewModel
        {
            Order = order,
            FlashMessage = TempData["Success"]?.ToString(),
            FlashError = TempData["Error"]?.ToString()
        };

        return View(viewModel);
    }

    [HttpPost("{id:long}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct = default)
    {
        var customerId = GetCurrentCustomerId();

        try
        {
            var success = await orderLifecycleService.CancelAsync(customerId, id, ct);
            if (!success)
            {
                TempData["Error"] = "Không thể hủy đơn hàng do đơn không tồn tại hoặc đã thay đổi trạng thái.";
            }
            else
            {
                TempData["Success"] = "Đã hủy đơn hàng và giải phóng ghế thành công.";
            }
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Code switch
            {
                "ORDER_EXPIRED" => "Đơn hàng đã hết thời gian giữ ghế và đã được đánh dấu hết hạn (EXPIRED).",
                "INVALID_ORDER_STATE" => "Không thể hủy đơn hàng vì đơn không ở trạng thái chờ thanh toán.",
                "FORBIDDEN" => "Bạn không có quyền thao tác trên đơn hàng này.",
                _ => ex.Message
            };
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private long GetCurrentCustomerId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(userIdStr, out var id)) return id;
        throw new UnauthorizedAccessException("Không xác định được định danh khách hàng.");
    }
}
