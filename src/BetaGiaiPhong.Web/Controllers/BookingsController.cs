using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Data.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("bookings")]
public sealed class BookingsController(SeatReservationService reservationService) : Controller
{
    [HttpPost("reserve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reserve(long showtimeId, string? seatIdsCsv, CancellationToken ct)
    {
        var seatIds = (seatIdsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (seatIds.Count is < 1 or > 8)
        {
            TempData["Error"] = "Vui lòng chọn từ 1 đến 8 ghế hợp lệ.";
            return RedirectToAction("Seats", "Showtimes", new { id = showtimeId });
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Url.Action("Seats", "Showtimes", new { id = showtimeId });
            return RedirectToAction("Login", "Account", new { returnUrl });
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdStr, out var customerId) || !User.IsInRole("CUSTOMER"))
        {
            TempData["Error"] = "Chỉ tài khoản Khách hàng (CUSTOMER) mới có quyền đặt vé.";
            return RedirectToAction("Seats", "Showtimes", new { id = showtimeId });
        }

        try
        {
            var result = await reservationService.ReserveAsync(customerId, showtimeId, seatIds, ct);
            TempData["Success"] = "Giữ ghế thành công! Vui lòng hoàn tất thanh toán trước khi hết hạn.";
            return RedirectToAction("Details", "Orders", new { id = result.OrderId });
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Code switch
            {
                "SEAT_NOT_AVAILABLE" => "Một hoặc nhiều ghế bạn chọn vừa có khách khác giữ hoặc đã bán. Vui lòng chọn ghế khác.",
                "SEAT_CONFLICT" => "Xung đột giữ ghế do nhiều yêu cầu đồng thời. Vui lòng thử lại.",
                "NOT_ON_SALE" => "Suất chiếu hiện không mở bán hoặc đã bắt đầu chiếu.",
                "SHOWTIME_NOT_FOUND" => "Không tìm thấy suất chiếu được yêu cầu.",
                "PRICE_NOT_CONFIGURED" => "Chưa cấu hình giá vé cho loại ghế đã chọn.",
                _ => ex.Message
            };
            return RedirectToAction("Seats", "Showtimes", new { id = showtimeId });
        }
    }
}
