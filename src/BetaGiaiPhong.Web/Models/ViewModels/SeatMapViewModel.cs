using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class SeatMapViewModel
{
    public SeatMapDto SeatMap { get; set; } = null!;
    public IReadOnlyList<string> DistinctRows { get; set; } = [];
    public short MaxColumnNo { get; set; }
}
