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

        var maxColumnNo = seatMap.Seats.Count > 0
            ? seatMap.Seats.Max(s => s.ColumnNo)
            : (short)0;

        var vm = new SeatMapViewModel
        {
            SeatMap = seatMap,
            DistinctRows = distinctRows,
            MaxColumnNo = maxColumnNo
        };

        return View(vm);
    }
}
