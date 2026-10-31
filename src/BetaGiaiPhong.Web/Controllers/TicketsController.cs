using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Authorize(Roles = "CUSTOMER")]
public sealed class TicketsController(ITicketQueryService ticketQueryService) : Controller
{
    [HttpGet("tickets/{ticketId:long}/qr")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetQrCode([FromRoute] long ticketId, CancellationToken ct)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(rawUserId, out var customerId) || customerId <= 0)
        {
            return NotFound();
        }

        var pngBytes = await ticketQueryService.GenerateQrCodePngBytesAsync(ticketId, customerId, ct);
        if (pngBytes is null)
        {
            return NotFound();
        }

        Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate");
        Response.Headers.Append("Pragma", "no-cache");
        return File(pngBytes, "image/png");
    }
}
