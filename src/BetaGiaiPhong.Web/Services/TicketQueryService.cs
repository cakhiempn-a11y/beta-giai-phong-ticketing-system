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

    private static DateTimeOffset ToVietnamOffset(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero).ToOffset(VietnamOffset);

    private static string FormatVietnamTime(DateTime utc)
        => ToVietnamOffset(utc).ToString("dd/MM/yyyy HH:mm");

    private static string FormatVietnamDateTimeWithSec(DateTime utc)
        => ToVietnamOffset(utc).ToString("dd/MM/yyyy HH:mm:ss");

    private static string FormatVietnamTimeOnly(DateTime utc)
        => ToVietnamOffset(utc).ToString("HH:mm");

    private static string FormatVietnamWeekdayDate(DateTime utc)
    {
        var vn = ToVietnamOffset(utc);
        var weekday = vn.DayOfWeek switch
        {
            DayOfWeek.Monday => "Thứ Hai",
            DayOfWeek.Tuesday => "Thứ Ba",
            DayOfWeek.Wednesday => "Thứ Tư",
            DayOfWeek.Thursday => "Thứ Năm",
            DayOfWeek.Friday => "Thứ Sáu",
            DayOfWeek.Saturday => "Thứ Bảy",
            _ => "Chủ Nhật"
        };
        return $"{weekday}, {vn:dd/MM/yyyy}";
    }

    public async Task<OrderTicketsViewModel?> GetCustomerOrderTicketsAsync(
        long customerId, long orderId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Showtime)
                .ThenInclude(s => s!.Movie)
            .Include(o => o.Showtime)
                .ThenInclude(s => s!.Room)
            .SingleOrDefaultAsync(o => o.OrderId == orderId && o.UserId == customerId, ct);

        if (order is null || order.Status != OrderStates.Paid) return null;

        var dtos = await GetCustomerTicketDtosAsync(customerId, orderId, ct);

        var showtime = order.Showtime;
        var movie = showtime?.Movie;
        var startUtc = showtime?.StartUtc ?? order.CreatedUtc;
        var endUtc = showtime?.EndUtc
            ?? (movie is { DurationMin: > 0 } ? startUtc.AddMinutes(movie.DurationMin) : startUtc.AddMinutes(110));

        var startVn = ToVietnamOffset(startUtc);
        var endVn = ToVietnamOffset(endUtc);
        bool crossesMidnight = endVn.Date > startVn.Date;

        var ratingDto = movie is not null
            ? CatalogService.BuildRatingDto(
                movie.RatingSource,
                movie.RatingValue,
                movie.RatingScaleMax,
                movie.RatingVoteCount,
                movie.RatingFetchedUtc)
            : null;

        var artworkDto = movie is not null ? CatalogService.BuildArtworkDto(movie) : null;

        return new OrderTicketsViewModel(
            OrderId: order.OrderId,
            OrderCode: order.OrderCode,
            OrderStatus: order.Status,
            MovieTitle: movie?.Title ?? "N/A",
            RoomName: showtime?.Room?.RoomName ?? "Phòng chiếu",
            StartUtc: startUtc,
            FormattedStartUtc: FormatVietnamTime(startUtc),
            Tickets: dtos,
            ShowtimeId: order.ShowtimeId,
            MovieId: movie?.MovieId ?? 0,
            MovieOriginalTitle: movie?.OriginalTitle,
            MovieReleaseYear: movie?.ReleaseYear,
            MovieDurationMin: movie?.DurationMin ?? 0,
            MovieDirectorNames: movie?.DirectorNames,
            MovieAgeLabel: movie?.AgeLabel ?? "P",
            MovieAgeLabelMarket: movie?.AgeLabelMarket,
            MovieRating: ratingDto,
            MovieArtwork: artworkDto,
            StartVietnamDateText: FormatVietnamWeekdayDate(startUtc),
            StartVietnamTimeOnlyText: FormatVietnamTimeOnly(startUtc),
            EndVietnamTimeOnlyText: FormatVietnamTimeOnly(endUtc),
            CrossesMidnight: crossesMidnight,
            PaidVietnamText: order.PaidUtc.HasValue ? FormatVietnamDateTimeWithSec(order.PaidUtc.Value) : null
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
                    where t.OrderId == orderId && o.UserId == customerId && o.Status == OrderStates.Paid
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
            TicketId: x.TicketId,
            TicketCode: x.TicketCode,
            OrderId: x.OrderId,
            OrderCode: x.OrderCode,
            ShowtimeId: x.ShowtimeId,
            MovieTitle: x.MovieTitle,
            RoomName: x.RoomName,
            StartUtc: x.StartUtc,
            FormattedStartUtc: FormatVietnamTime(x.StartUtc),
            SeatId: x.SeatId,
            SeatCode: x.SeatCode,
            SeatType: x.SeatType,
            PriceVnd: x.PriceVnd,
            Status: x.Status,
            IssuedUtc: x.IssuedUtc,
            UsedUtc: x.UsedUtc,
            QrCodeUrl: $"/tickets/{x.TicketId}/qr",
            IssuedVietnamText: FormatVietnamDateTimeWithSec(x.IssuedUtc),
            UsedVietnamText: x.UsedUtc.HasValue ? FormatVietnamDateTimeWithSec(x.UsedUtc.Value) : null
        )).ToList();
    }

    public async Task<byte[]?> GenerateQrCodePngBytesAsync(
        long ticketId, long customerId, CancellationToken ct = default)
    {
        // Fail-closed: Customer ID must be positive and valid
        if (customerId <= 0) return null;

        var ticket = await db.Tickets
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TicketId == ticketId, ct);

        if (ticket is null) return null;

        // Fail-closed: Enforce that the ticket's order belongs to customerId and is PAID
        var order = await db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.OrderId == ticket.OrderId && o.UserId == customerId, ct);

        if (order is null || order.Status != OrderStates.Paid)
        {
            return null;
        }

        // Only generate QR for valid active/used tickets
        if (ticket.Status is not ("ISSUED" or "USED"))
        {
            return null;
        }

        // Decrypt the raw token in memory
        var rawToken = qrProtector.Unseal(ticket.QrTokenCipher);

        // Generate QR code using pure C# QRCoder PngByteQRCode (no native/GDI dependencies).
        // ECCLevel.M yields QR Version 3 (29x29 + 8 quiet zone = 37x37 modules) for 43-char Base64URL tokens,
        // and 7px per module (259x259 px) stays below ZXing HybridBinarizer's 8x8 block size.
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(rawToken, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(7);
    }
}
