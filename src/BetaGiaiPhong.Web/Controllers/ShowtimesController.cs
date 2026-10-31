using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("showtimes")]
public sealed class ShowtimesController(ICatalogService catalogService) : Controller
{
    [HttpGet("{id:long}/seats")]
    public async Task<IActionResult> Seats(long id, CancellationToken ct)
    {
        var seatMap = await catalogService.GetShowtimeSeatMapAsync(id, ct);
        if (seatMap is null)
        {
            return NotFound();
        }

        var distinctRows = seatMap.Seats
            .Select(s => s.RowLabel)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        var vm = new SeatMapViewModel
        {
            SeatMap = seatMap,
            DistinctRows = distinctRows
        };

        return View(vm);
    }
}
