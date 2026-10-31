using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Authorize]
public sealed class TicketsController(ITicketQueryService ticketQueryService) : Controller
{
    [HttpGet("tickets/{ticketId:long}/qr")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetQrCode([FromRoute] long ticketId, CancellationToken ct)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        long.TryParse(rawUserId, out var customerId);
        var isStaffOrHigher = User.IsInRole("STAFF") || User.IsInRole("MANAGER") || User.IsInRole("ADMIN");

        var pngBytes = await ticketQueryService.GenerateQrCodePngBytesAsync(
            ticketId, customerId > 0 ? customerId : null, isStaffOrHigher, ct);

        if (pngBytes is null)
        {
            return NotFound();
        }

        Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate");
        Response.Headers.Append("Pragma", "no-cache");
        return File(pngBytes, "image/png");
    }
}
