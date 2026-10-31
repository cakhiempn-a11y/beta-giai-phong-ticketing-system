using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Services;

public interface IBookingQueryService
{
    Task<(IReadOnlyList<OrderSummaryDto> Orders, int TotalCount)> GetCustomerOrdersAsync(
        long customerId, int page = 1, int pageSize = 10, CancellationToken ct = default);

    Task<OrderDetailDto?> GetCustomerOrderDetailAsync(
        long customerId, long orderId, CancellationToken ct = default);
}

public sealed class BookingQueryService(BetaDbContext db, ISystemClock clock) : IBookingQueryService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static string FormatVietnamTime(DateTime utc)
    {
        var dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
        return dto.ToString("dd/MM/yyyy HH:mm");
    }

    public async Task<(IReadOnlyList<OrderSummaryDto> Orders, int TotalCount)> GetCustomerOrdersAsync(
        long customerId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;

        var now = clock.UtcNow;

        var query = db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == customerId);

        var totalCount = await query.CountAsync(ct);

        var orders = await query
            .OrderByDescending(o => o.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.OrderId,
                o.OrderCode,
                o.ShowtimeId,
                MovieTitle = o.Showtime != null && o.Showtime.Movie != null ? o.Showtime.Movie.Title : "N/A",
                StartUtc = o.Showtime != null ? o.Showtime.StartUtc : o.CreatedUtc,
                o.SeatCount,
                o.TotalVnd,
                o.Status,
                o.CreatedUtc,
                o.ExpiresUtc
            })
            .ToListAsync(ct);

        var dtos = orders.Select(o =>
        {
            var isHoldExpired = o.Status == OrderStates.Pending && o.ExpiresUtc <= now;
            var canCancel = o.Status == OrderStates.Pending && o.ExpiresUtc > now;

            return new OrderSummaryDto(
                o.OrderId,
                o.OrderCode,
                o.ShowtimeId,
                o.MovieTitle,
                o.StartUtc,
                FormatVietnamTime(o.StartUtc),
                o.SeatCount,
                o.TotalVnd,
                o.Status,
                o.CreatedUtc,
                o.ExpiresUtc,
                isHoldExpired,
                canCancel
            );
        }).ToList();

        return (dtos, totalCount);
    }

    public async Task<OrderDetailDto?> GetCustomerOrderDetailAsync(
        long customerId, long orderId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId && o.UserId == customerId)
            .Select(o => new
            {
                o.OrderId,
                o.OrderCode,
                o.Status,
                o.ShowtimeId,
                MovieTitle = o.Showtime != null && o.Showtime.Movie != null ? o.Showtime.Movie.Title : "N/A",
                RoomName = o.Showtime != null && o.Showtime.Room != null ? o.Showtime.Room.RoomName : "Phòng chiếu",
                StartUtc = o.Showtime != null ? o.Showtime.StartUtc : o.CreatedUtc,
                o.SubtotalVnd,
                o.FeeVnd,
                o.DiscountVnd,
                o.TotalVnd,
                o.CreatedUtc,
                o.ExpiresUtc
            })
            .SingleOrDefaultAsync(ct);

        if (order is null) return null;

        var seats = await (from os in db.OrderSeats.AsNoTracking()
            join s in db.Seats.AsNoTracking() on os.SeatId equals s.SeatId
            where os.OrderId == orderId
            orderby s.RowLabel, s.ColumnNo
            select new OrderSeatItemDto(
                os.SeatId,
                s.SeatCode,
                os.SeatTypeSnapshot,
                os.UnitPriceVnd
            )).ToListAsync(ct);

        var isHoldExpired = order.Status == OrderStates.Pending && order.ExpiresUtc <= now;
        var canCancel = order.Status == OrderStates.Pending && order.ExpiresUtc > now;
        var remainingSeconds = (int)Math.Max(0, (order.ExpiresUtc - now).TotalSeconds);

        return new OrderDetailDto(
            order.OrderId,
            order.OrderCode,
            order.Status,
            order.ShowtimeId,
            order.MovieTitle,
            order.RoomName,
            order.StartUtc,
            FormatVietnamTime(order.StartUtc),
            seats,
            order.SubtotalVnd,
            order.FeeVnd,
            order.DiscountVnd,
            order.TotalVnd,
            order.CreatedUtc,
            order.ExpiresUtc,
            remainingSeconds,
            isHoldExpired,
            canCancel
        );
    }
}
