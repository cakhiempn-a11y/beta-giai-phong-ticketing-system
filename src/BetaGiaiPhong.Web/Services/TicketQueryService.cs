using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;
using QRCoder;

namespace BetaGiaiPhong.Web.Services;

public sealed class TicketQueryService(BetaDbContext db, QrTokenProtector qrProtector) : ITicketQueryService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static string FormatVietnamTime(DateTime utc)
    {
        var dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);
        return dto.ToString("dd/MM/yyyy HH:mm");
    }

    public async Task<OrderTicketsViewModel?> GetCustomerOrderTicketsAsync(
        long customerId, long orderId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId && o.UserId == customerId)
            .Select(o => new
            {
                o.OrderId,
                o.OrderCode,
                o.Status,
                ShowtimeId = o.ShowtimeId,
                MovieTitle = o.Showtime != null && o.Showtime.Movie != null ? o.Showtime.Movie.Title : "N/A",
                RoomName = o.Showtime != null && o.Showtime.Room != null ? o.Showtime.Room.RoomName : "Phòng chiếu",
                StartUtc = o.Showtime != null ? o.Showtime.StartUtc : o.CreatedUtc
            })
            .SingleOrDefaultAsync(ct);

        if (order is null) return null;

        var dtos = await GetCustomerTicketDtosAsync(customerId, orderId, ct);

        return new OrderTicketsViewModel(
            order.OrderId,
            order.OrderCode,
            order.Status,
            order.MovieTitle,
            order.RoomName,
            order.StartUtc,
            FormatVietnamTime(order.StartUtc),
            dtos
        );
    }

    public async Task<IReadOnlyList<CustomerTicketDto>> GetCustomerTicketDtosAsync(
        long customerId, long orderId, CancellationToken ct = default)
    {
        var query = from t in db.Tickets.AsNoTracking()
                    join o in db.Orders.AsNoTracking() on t.OrderId equals o.OrderId
                    join st in db.Showtimes.AsNoTracking() on t.ShowtimeId equals st.ShowtimeId
                    join m in db.Movies.AsNoTracking() on st.MovieId equals m.MovieId into movies
                    from m in movies.DefaultIfEmpty()
                    join r in db.Rooms.AsNoTracking() on st.RoomId equals r.RoomId into rooms
                    from r in rooms.DefaultIfEmpty()
                    join s in db.Seats.AsNoTracking() on t.SeatId equals s.SeatId
                    join os in db.OrderSeats.AsNoTracking() on new { t.OrderId, t.SeatId } equals new { os.OrderId, os.SeatId } into orderSeats
                    from os in orderSeats.DefaultIfEmpty()
                    where t.OrderId == orderId && o.UserId == customerId
                    orderby s.RowLabel, s.ColumnNo
                    select new
                    {
                        t.TicketId,
                        t.TicketCode,
                        t.OrderId,
                        o.OrderCode,
                        t.ShowtimeId,
                        MovieTitle = m != null ? m.Title : "N/A",
                        RoomName = r != null ? r.RoomName : "Phòng chiếu",
                        st.StartUtc,
                        t.SeatId,
                        s.SeatCode,
                        SeatType = os != null ? os.SeatTypeSnapshot : "STANDARD",
                        PriceVnd = os != null ? os.UnitPriceVnd : 0,
                        t.Status,
                        t.IssuedUtc,
                        t.UsedUtc
                    };

        var items = await query.ToListAsync(ct);

        return items.Select(x => new CustomerTicketDto(
            x.TicketId,
            x.TicketCode,
            x.OrderId,
            x.OrderCode,
            x.ShowtimeId,
            x.MovieTitle,
            x.RoomName,
            x.StartUtc,
            FormatVietnamTime(x.StartUtc),
            x.SeatId,
            x.SeatCode,
            x.SeatType,
            x.PriceVnd,
            x.Status,
            x.IssuedUtc,
            x.UsedUtc,
            $"/tickets/{x.TicketId}/qr"
        )).ToList();
    }

    public async Task<byte[]?> GenerateQrCodePngBytesAsync(
        long ticketId, long? customerId, bool isStaffOrHigher = false, CancellationToken ct = default)
    {
        var ticket = await db.Tickets
            .AsNoTracking()
            .Include(t => t.OrderSeat)
            .SingleOrDefaultAsync(t => t.TicketId == ticketId, ct);

        if (ticket is null) return null;

        // If not staff, enforce customer ownership of the ticket's order
        if (!isStaffOrHigher && customerId.HasValue)
        {
            var isOwner = await db.Orders
                .AsNoTracking()
                .AnyAsync(o => o.OrderId == ticket.OrderId && o.UserId == customerId.Value, ct);

            if (!isOwner) return null;
        }

        // Only generate QR for valid active/used tickets
        if (ticket.Status is not ("ISSUED" or "USED"))
        {
            return null;
        }

        // Decrypt the raw token in memory
        var rawToken = qrProtector.Unseal(ticket.QrTokenCipher);

        // Generate QR code using pure C# QRCoder PngByteQRCode (no native/GDI dependencies)
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(rawToken, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }
}
