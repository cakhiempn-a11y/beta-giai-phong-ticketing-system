using BetaGiaiPhong.Data.Services;

namespace BetaGiaiPhong.Tests.Helpers;

public sealed class TestClock(DateTime utcNow) : ISystemClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
