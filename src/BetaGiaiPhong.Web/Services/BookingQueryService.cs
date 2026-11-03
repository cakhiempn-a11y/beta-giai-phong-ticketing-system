using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Models.ViewModels;

namespace BetaGiaiPhong.Web.Services;

public interface IBookingQueryService
{
    Task<(IReadOnlyList<OrderSummaryDto> Orders, int TotalCount)> GetCustomerOrdersAsync(
        long customerId, int page = 1, int pageSize = 10, CancellationToken ct = default, string? statusFilter = null);

    Task<OrderDetailDto?> GetCustomerOrderDetailAsync(
        long customerId, long orderId, CancellationToken ct = default);

    Task<CheckoutViewModel?> GetCustomerOrderCheckoutAsync(
        long customerId, long orderId, string? attemptKey = null, bool isSimulatorEnabled = false, CancellationToken ct = default);
}

public sealed class BookingQueryService(BetaDbContext db, ISystemClock clock) : IBookingQueryService
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

    private static string FormatVietnamTimeWithSecOnly(DateTime utc)
        => ToVietnamOffset(utc).ToString("HH:mm:ss");

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

    public async Task<(IReadOnlyList<OrderSummaryDto> Orders, int TotalCount)> GetCustomerOrdersAsync(
        long customerId, int page = 1, int pageSize = 10, CancellationToken ct = default, string? statusFilter = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 10;

        var now = clock.UtcNow;

        var query = db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == customerId);

        var normalizedFilter = statusFilter?.Trim().ToLowerInvariant();
        if (normalizedFilter == "paid")
        {
            query = query.Where(o => o.Status == OrderStates.Paid);
        }
        else if (normalizedFilter == "pending")
        {
            query = query.Where(o => o.Status == OrderStates.Pending && o.ExpiresUtc > now);
        }
        else if (normalizedFilter == "inactive")
        {
            query = query.Where(o =>
                o.Status == OrderStates.Cancelled ||
                o.Status == OrderStates.Expired ||
                o.Status == OrderStates.Failed ||
                o.Status == OrderStates.Review ||
                (o.Status == OrderStates.Pending && o.ExpiresUtc <= now));
        }

        var totalCount = await query.CountAsync(ct);

        var orders = await query
            .Include(o => o.Showtime)
                .ThenInclude(s => s!.Movie)
            .Include(o => o.Showtime)
                .ThenInclude(s => s!.Room)
            .OrderByDescending(o => o.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.OrderId).ToList();
        var seatLookup = new Dictionary<long, string>();
        if (orderIds.Count > 0)
        {
            var seatRows = await (from os in db.OrderSeats.AsNoTracking()
                join s in db.Seats.AsNoTracking() on os.SeatId equals s.SeatId
                where orderIds.Contains(os.OrderId)
                orderby os.OrderId, s.RowLabel, s.ColumnNo
                select new { os.OrderId, s.SeatCode }).ToListAsync(ct);

            foreach (var grp in seatRows.GroupBy(x => x.OrderId))
            {
                seatLookup[grp.Key] = string.Join(", ", grp.Select(x => x.SeatCode));
            }
        }

        var dtos = orders.Select(o =>
        {
            var isHoldExpired = o.Status == OrderStates.Pending && o.ExpiresUtc <= now;
            var canCancel = o.Status == OrderStates.Pending && o.ExpiresUtc > now;
            var showtime = o.Showtime;
            var movie = showtime?.Movie;
            var startUtc = showtime?.StartUtc ?? o.CreatedUtc;
            var artwork = movie is not null ? CatalogService.BuildArtworkDto(movie) : null;

            return new OrderSummaryDto(
                OrderId: o.OrderId,
                OrderCode: o.OrderCode,
                ShowtimeId: o.ShowtimeId,
                MovieTitle: movie?.Title ?? "N/A",
                StartUtc: startUtc,
                StartVietnamText: FormatVietnamTime(startUtc),
                SeatCount: o.SeatCount,
                TotalVnd: o.TotalVnd,
                Status: o.Status,
                CreatedUtc: o.CreatedUtc,
                ExpiresUtc: o.ExpiresUtc,
                IsHoldExpired: isHoldExpired,
                CanCancel: canCancel,
                CreatedVietnamText: FormatVietnamDateTimeWithSec(o.CreatedUtc),
                RoomName: showtime?.Room?.RoomName ?? "Phòng chiếu",
                SeatCodesSummary: seatLookup.TryGetValue(o.OrderId, out var codes) ? codes : "",
                MovieId: movie?.MovieId ?? 0,
                MovieOriginalTitle: movie?.OriginalTitle,
                MovieReleaseYear: movie?.ReleaseYear,
                MovieAgeLabel: movie?.AgeLabel ?? "P",
                MovieAgeLabelMarket: movie?.AgeLabelMarket,
                MovieArtwork: artwork
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
            .Include(o => o.Showtime)
                .ThenInclude(s => s!.Movie)
            .Include(o => o.Showtime)
                .ThenInclude(s => s!.Room)
            .SingleOrDefaultAsync(o => o.OrderId == orderId && o.UserId == customerId, ct);

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
        var resolvedPosterUrl = artworkDto is { HasVerifiedPoster: true }
            ? (artworkDto.PosterW342Url ?? artworkDto.PosterUrl)
            : null;

        return new OrderDetailDto(
            OrderId: order.OrderId,
            OrderCode: order.OrderCode,
            OrderStatus: order.Status,
            ShowtimeId: order.ShowtimeId,
            MovieTitle: movie?.Title ?? "N/A",
            RoomName: showtime?.Room?.RoomName ?? "Phòng chiếu",
            StartUtc: startUtc,
            StartVietnamText: FormatVietnamTime(startUtc),
            Seats: seats,
            SubtotalVnd: order.SubtotalVnd,
            FeeVnd: order.FeeVnd,
            DiscountVnd: order.DiscountVnd,
            TotalVnd: order.TotalVnd,
            CreatedUtc: order.CreatedUtc,
            ExpiresUtc: order.ExpiresUtc,
            RemainingSeconds: remainingSeconds,
            IsHoldExpired: isHoldExpired,
            CanCancel: canCancel,
            CreatedVietnamText: FormatVietnamDateTimeWithSec(order.CreatedUtc),
            ExpiresVietnamText: FormatVietnamDateTimeWithSec(order.ExpiresUtc),
            ExpiresVietnamTimeOnlyText: FormatVietnamTimeWithSecOnly(order.ExpiresUtc),
            PaidUtc: order.PaidUtc,
            PaidVietnamText: order.PaidUtc.HasValue ? FormatVietnamDateTimeWithSec(order.PaidUtc.Value) : null,
            EndUtc: endUtc,
            StartVietnamDateText: FormatVietnamWeekdayDate(startUtc),
            StartVietnamTimeOnlyText: FormatVietnamTimeOnly(startUtc),
            EndVietnamTimeOnlyText: FormatVietnamTimeOnly(endUtc),
            CrossesMidnight: crossesMidnight,
            MovieId: movie?.MovieId ?? 0,
            MovieOriginalTitle: movie?.OriginalTitle,
            MovieReleaseYear: movie?.ReleaseYear,
            MovieDurationMin: movie?.DurationMin ?? 0,
            MovieDirectorNames: movie?.DirectorNames,
            MovieGenreText: movie?.GenreText,
            MovieAgeLabel: movie?.AgeLabel ?? "P",
            MovieAgeLabelMarket: movie?.AgeLabelMarket,
            PosterUrl: resolvedPosterUrl,
            MovieRating: ratingDto,
            MovieArtwork: artworkDto
        );
    }

    public async Task<CheckoutViewModel?> GetCustomerOrderCheckoutAsync(
        long customerId, long orderId, string? attemptKey = null, bool isSimulatorEnabled = false, CancellationToken ct = default)
    {
        var detail = await GetCustomerOrderDetailAsync(customerId, orderId, ct);
        if (detail is null) return null;

        var resolvedAttemptKey = string.IsNullOrWhiteSpace(attemptKey)
            ? "ATTEMPT-" + Guid.NewGuid().ToString("N")
            : attemptKey;

        return new CheckoutViewModel(
            OrderId: detail.OrderId,
            OrderCode: detail.OrderCode,
            MovieTitle: detail.MovieTitle,
            RoomName: detail.RoomName,
            StartUtc: detail.StartUtc,
            FormattedStartUtc: detail.StartVietnamText,
            Seats: detail.Seats,
            SubtotalVnd: detail.SubtotalVnd,
            FeeVnd: detail.FeeVnd,
            DiscountVnd: detail.DiscountVnd,
            TotalVnd: detail.TotalVnd,
            ExpiresUtc: detail.ExpiresUtc,
            RemainingSeconds: detail.RemainingSeconds,
            IsExpired: detail.IsHoldExpired,
            AttemptKey: resolvedAttemptKey,
            IsSimulatorEnabled: isSimulatorEnabled,
            ShowtimeId: detail.ShowtimeId,
            MovieId: detail.MovieId,
            CreatedVietnamText: detail.CreatedVietnamText,
            ExpiresVietnamText: detail.ExpiresVietnamText,
            ExpiresVietnamTimeOnlyText: detail.ExpiresVietnamTimeOnlyText,
            StartVietnamDateText: detail.StartVietnamDateText,
            StartVietnamTimeOnlyText: detail.StartVietnamTimeOnlyText,
            EndVietnamTimeOnlyText: detail.EndVietnamTimeOnlyText,
            CrossesMidnight: detail.CrossesMidnight,
            MovieOriginalTitle: detail.MovieOriginalTitle,
            MovieReleaseYear: detail.MovieReleaseYear,
            MovieDurationMin: detail.MovieDurationMin,
            MovieDirectorNames: detail.MovieDirectorNames,
            MovieGenreText: detail.MovieGenreText,
            MovieAgeLabel: detail.MovieAgeLabel,
            MovieAgeLabelMarket: detail.MovieAgeLabelMarket,
            PosterUrl: detail.PosterUrl,
            MovieRating: detail.MovieRating,
            MovieArtwork: detail.MovieArtwork
        );
    }
}
