using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("orders")]
[Authorize(Policy = "RequireCustomer")]
public sealed class OrdersController(
    IBookingQueryService bookingQueryService,
    ITicketQueryService ticketQueryService,
    OrderLifecycleService orderLifecycleService,
    PaymentService paymentService,
    BetaDbContext db,
    IWebHostEnvironment env,
    IConfiguration config,
    ILogger<OrdersController> logger) : Controller
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

    [HttpGet("{id:long}/checkout")]
    public async Task<IActionResult> Checkout(long id, CancellationToken ct = default)
    {
        var customerId = GetCurrentCustomerId();
        var isSimEnabled = env.IsDevelopment() && string.Equals(
            config["BGP_ENABLE_MOCK_PAYMENT_SIMULATOR"] ?? Environment.GetEnvironmentVariable("BGP_ENABLE_MOCK_PAYMENT_SIMULATOR"),
            "true", StringComparison.OrdinalIgnoreCase);

        var checkout = await bookingQueryService.GetCustomerOrderCheckoutAsync(
            customerId, id, null, isSimEnabled, ct);

        if (checkout is null)
        {
            TempData["Error"] = "Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn.";
            return RedirectToAction(nameof(Index));
        }

        // Fetch current status to decide flow
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OrderId == id, ct);
        if (order != null && order.Status == OrderStates.Paid)
        {
            TempData["Success"] = "Đơn hàng này đã được thanh toán thành công.";
            return RedirectToAction(nameof(Tickets), new { id });
        }

        if (order != null && order.Status != OrderStates.Pending)
        {
            TempData["Error"] = $"Đơn hàng không ở trạng thái chờ thanh toán (trạng thái: {order.Status}).";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (checkout.IsExpired)
        {
            TempData["Error"] = "Đơn hàng đã hết thời gian giữ ghế và không thể tiến hành thanh toán.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(checkout);
    }

    [HttpPost("{id:long}/demo-pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoPay(long id, [FromForm] string? outcome = "SUCCESS", CancellationToken ct = default)
    {
        var isSimEnabled = env.IsDevelopment() && string.Equals(
            config["BGP_ENABLE_MOCK_PAYMENT_SIMULATOR"] ?? Environment.GetEnvironmentVariable("BGP_ENABLE_MOCK_PAYMENT_SIMULATOR"),
            "true", StringComparison.OrdinalIgnoreCase);

        if (!isSimEnabled)
        {
            return NotFound();
        }

        var customerId = GetCurrentCustomerId();
        logger.LogInformation("Customer {CustomerId} initiated demo pay for order {OrderId} with outcome {Outcome}.", customerId, id, outcome);

        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.OrderId == id, ct);
        if (order is null || order.UserId != customerId)
        {
            TempData["Error"] = "Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn.";
            return RedirectToAction(nameof(Index));
        }

        if (order.Status != OrderStates.Pending)
        {
            TempData["Error"] = $"Đơn hàng không ở trạng thái chờ thanh toán (trạng thái: {order.Status}).";
            return RedirectToAction(nameof(Details), new { id });
        }

        var attemptKey = "SIM-ATTEMPT-" + Guid.NewGuid().ToString("N");

        try
        {
            // 1. Create mock attempt in domain service
            await paymentService.CreateMockAttemptAsync(customerId, id, attemptKey, ct);

            // 2. Build mock payment webhook message
            var targetOutcome = string.Equals(outcome, "FAILED", StringComparison.OrdinalIgnoreCase) ? "FAILED" : "SUCCESS";
            var message = new MockPaymentMessage(
                ProviderEventRef: "SIM-EVT-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                AttemptKey: attemptKey,
                ProviderTransactionRef: "SIM-TXN-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                OrderCode: order.OrderCode,
                AmountVnd: order.TotalVnd,
                Outcome: targetOutcome
            );

            // 3. Cryptographically sign in Development environment
            var signature = MockPaymentSecurity.SignForDevelopment(message);

            // 4. Verify HMAC before applying to domain service
            var verified = MockPaymentSecurity.Verify(message, signature);

            // 5. Apply verified message through standard PaymentService
            var result = await paymentService.ApplyVerifiedMockAsync(verified, ct);

            if (result.OrderStatus == OrderStates.Paid)
            {
                TempData["Success"] = "Thanh toán mô phỏng thành công! Đơn hàng đã được ghi nhận PAID và vé điện tử đã được phát hành.";
                return RedirectToAction(nameof(Tickets), new { id });
            }

            if (result.OrderStatus == OrderStates.Failed)
            {
                TempData["Error"] = "Thanh toán mô phỏng thất bại. Đơn hàng chuyển sang FAILED và ghế đã được giải phóng.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (result.OrderStatus == OrderStates.Review)
            {
                TempData["Warning"] = "Thanh toán cho đơn quá hạn. Đơn hàng chuyển sang trạng thái cần đối soát (NEEDS_REVIEW).";
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["Warning"] = $"Kết quả xử lý thanh toán: {result.Decision} (Trạng thái: {result.OrderStatus}).";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Code switch
            {
                "ORDER_UNAVAILABLE" => "Đơn hàng không thể thanh toán do đã quá thời gian giữ ghế hoặc không thuộc về bạn.",
                _ => ex.Message
            };
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpGet("{id:long}/tickets")]
    public async Task<IActionResult> Tickets(long id, CancellationToken ct = default)
    {
        var customerId = GetCurrentCustomerId();
        var ticketsModel = await ticketQueryService.GetCustomerOrderTicketsAsync(customerId, id, ct);

        if (ticketsModel is null)
        {
            TempData["Error"] = "Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn.";
            return RedirectToAction(nameof(Index));
        }

        if (ticketsModel.OrderStatus != OrderStates.Paid)
        {
            TempData["Warning"] = $"Vé điện tử chỉ được phát hành cho đơn hàng đã thanh toán thành công (PAID). Trạng thái hiện tại: {ticketsModel.OrderStatus}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(ticketsModel);
    }

    [HttpPost("{id:long}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct = default)
    {
        var customerId = GetCurrentCustomerId();
        logger.LogInformation("Customer {CustomerId} initiated cancel for order {OrderId}.", customerId, id);

        try
        {
            var success = await orderLifecycleService.CancelAsync(customerId, id, ct);
            if (!success)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Đã hủy đơn hàng và giải phóng ghế thành công.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (BusinessRuleException ex)
        {
            if (ex.Code == "ORDER_NOT_FOUND")
            {
                TempData["Error"] = "Không tìm thấy đơn hàng yêu cầu hoặc đơn hàng không thuộc về bạn.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Error"] = ex.Code switch
            {
                "ORDER_EXPIRED" => "Đơn hàng đã hết thời gian giữ ghế và đã được đánh dấu hết hạn (EXPIRED).",
                "INVALID_ORDER_STATE" => "Không thể hủy đơn hàng vì đơn không ở trạng thái chờ thanh toán.",
                _ => ex.Message
            };
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private long GetCurrentCustomerId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(userIdStr, out var id)) return id;
        throw new UnauthorizedAccessException("Không xác định được định danh khách hàng.");
    }
}
