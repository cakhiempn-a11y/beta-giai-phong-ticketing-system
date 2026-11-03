using System.Security.Claims;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Web.Controllers;

[Authorize(Policy = "RequireManager")]
[Route("management")]
public sealed class ManagementController(
    BetaDbContext db,
    MovieManagementService movieService,
    MovieContentSyncService syncService,
    ShowtimePublishingService publishingService,
    ManagementQueryService queryService,
    ISystemClock clock,
    ILogger<ManagementController> logger) : Controller
{
    private long GetCurrentManagerId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(raw, out var id) || id <= 0)
        {
            throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ hoặc thiếu User ID.");
        }
        return id;
    }

    private static DateTime ConvertVnTimeToUtc(DateTime vnLocalTime)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(vnLocalTime, DateTimeKind.Unspecified), tz);
        }
        catch
        {
            return DateTime.SpecifyKind(vnLocalTime.AddHours(-7), DateTimeKind.Utc);
        }
    }

    private ManageMovieFormViewModel MapCreateRequestToFormVm(CreateMovieRequest request) => new()
    {
        Title = request.Title,
        DurationMin = request.DurationMin,
        GenreText = request.GenreText,
        AgeLabel = request.AgeLabel,
        Synopsis = request.Synopsis,
        PosterUrl = request.PosterUrl,
        OriginalTitle = request.OriginalTitle,
        ReleaseYear = request.ReleaseYear,
        DirectorNames = request.DirectorNames,
        OriginCountry = request.OriginCountry,
        OriginalLanguage = request.OriginalLanguage,
        WikidataQid = request.WikidataQid,
        ImdbId = request.ImdbId,
        TmdbId = request.TmdbId,
        BackdropUrl = request.BackdropUrl,
        OfficialTrailerYoutubeId = request.OfficialTrailerYoutubeId,
        ManualOverrideMask = request.ManualOverrides is { Count: > 0 } ? string.Join(",", request.ManualOverrides) : null,
        IsTmdbConfigured = syncService.IsTmdbConfigured
    };

    private ManageMovieFormViewModel MapEditRequestToFormVm(long id, EditMovieRequest request, bool hasActiveShowtimes, Movie? existing = null) => new()
    {
        MovieId = id,
        Title = request.Title,
        DurationMin = request.DurationMin,
        GenreText = request.GenreText,
        AgeLabel = request.AgeLabel,
        Synopsis = request.Synopsis,
        PosterUrl = request.PosterUrl,
        Status = existing?.Status ?? "DRAFT",
        HasActiveShowtimes = hasActiveShowtimes,
        IsTmdbConfigured = syncService.IsTmdbConfigured,
        OriginalTitle = request.OriginalTitle,
        ReleaseYear = request.ReleaseYear,
        DirectorNames = request.DirectorNames,
        OriginCountry = request.OriginCountry,
        OriginalLanguage = request.OriginalLanguage,
        WikidataQid = request.WikidataQid,
        ImdbId = request.ImdbId,
        TmdbId = request.TmdbId,
        BackdropUrl = request.BackdropUrl,
        OfficialTrailerYoutubeId = request.OfficialTrailerYoutubeId,
        ArtworkSource = existing?.ArtworkSource,
        MetadataSource = existing?.MetadataSource,
        RatingSource = existing?.RatingSource,
        RatingValue = existing?.RatingValue,
        RatingVoteCount = existing?.RatingVoteCount,
        ExternalSyncedUtc = existing?.ExternalSyncedUtc,
        ManualOverrideMask = request.ApplyOverrideLocks
            ? (request.ManualOverrides is { Count: > 0 } ? string.Join(",", request.ManualOverrides) : null)
            : existing?.ManualOverrideMask
    };

    // =========================================================================
    // 1. DASHBOARD & OVERVIEW
    // =========================================================================

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Reports));

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] long? movieId,
        [FromQuery] long? roomId,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        DateTime? fromUtc = null;
        DateTime? toUtc = null;

        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var parsedFrom))
        {
            fromUtc = ConvertVnTimeToUtc(parsedFrom.Date);
        }

        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var parsedTo))
        {
            toUtc = ConvertVnTimeToUtc(parsedTo.Date.AddDays(1).AddTicks(-1));
        }

        var report = await queryService.GetSalesDashboardAsync(
            fromUtc, toUtc, movieId, roomId, page, 15, ct);

        var movies = await db.Movies.AsNoTracking().OrderBy(m => m.Title).ToListAsync(ct);
        var rooms = await db.Rooms.AsNoTracking().OrderBy(r => r.RoomName).ToListAsync(ct);

        var vm = new ManageDashboardViewModel
        {
            FromDateVn = fromDate,
            ToDateVn = toDate,
            SelectedMovieId = movieId,
            SelectedRoomId = roomId,
            Movies = movies,
            Rooms = rooms,
            Report = report
        };

        return View(vm);
    }

    // =========================================================================
    // 2. MOVIE MANAGEMENT & EXTERNAL PROVENANCE SYNC
    // =========================================================================

    [HttpGet("movies")]
    public async Task<IActionResult> Movies(
        [FromQuery] string? search,
        [FromQuery] string? status = "ALL",
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var result = await queryService.GetMoviesAsync(search, status, page, 15, ct);

        var vm = new ManageMoviesViewModel
        {
            Items = result.Items,
            Search = search,
            StatusFilter = status ?? "ALL",
            CurrentPage = result.Page,
            TotalPages = result.TotalPages,
            TotalCount = result.TotalCount,
            IsTmdbConfigured = syncService.IsTmdbConfigured
        };

        return View(vm);
    }

    [HttpGet("movies/create")]
    public IActionResult MovieCreate()
    {
        return View(new ManageMovieFormViewModel
        {
            IsTmdbConfigured = syncService.IsTmdbConfigured
        });
    }

    [HttpPost("movies/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MovieCreate(
        [FromForm] CreateMovieRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(MapCreateRequestToFormVm(request));
        }

        try
        {
            var managerId = GetCurrentManagerId();
            var provenance = new MovieProvenanceEditInput(
                OriginalTitle: request.OriginalTitle,
                ReleaseYear: request.ReleaseYear,
                DirectorNames: request.DirectorNames,
                OriginCountry: request.OriginCountry,
                OriginalLanguage: request.OriginalLanguage,
                WikidataQid: request.WikidataQid,
                ImdbId: request.ImdbId,
                TmdbId: request.TmdbId,
                BackdropUrl: request.BackdropUrl,
                OfficialTrailerYoutubeId: request.OfficialTrailerYoutubeId,
                ApplyExplicitOverrideLocks: request.ManualOverrides is { Count: > 0 },
                ExplicitOverrideLocks: request.ManualOverrides);

            var movieId = await movieService.CreateMovieAsync(
                managerId, request.Title, request.DurationMin,
                request.GenreText, request.AgeLabel, request.Synopsis,
                request.PosterUrl, ct, provenance);

            logger.LogInformation("Manager {ManagerId} created movie {MovieId} ({Title})", managerId, movieId, request.Title);
            TempData["Success"] = $"Đã tạo phim '{request.Title}' thành công ở trạng thái DRAFT.";
            return RedirectToAction(nameof(MovieDetail), new { id = movieId });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(MapCreateRequestToFormVm(request));
        }
    }

    [HttpPost("movies/sync-external")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncExternalMovie(
        [FromForm] SyncExternalMovieRequest request,
        CancellationToken ct)
    {
        var managerId = GetCurrentManagerId();
        var result = await syncService.SyncExternalMovieAsync(
            request.VerifiedKey,
            request.TmdbId,
            managerId,
            request.TargetMovieId,
            ct);

        if (result.Outcome is "CREATED_DRAFT" or "UPDATED")
        {
            TempData["Success"] = result.Message;
            if (result.MovieId.HasValue)
            {
                return RedirectToAction(nameof(MovieDetail), new { id = result.MovieId.Value });
            }
        }
        else if (result.Outcome == "MISSING_API_KEY")
        {
            TempData["Warning"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        if (request.TargetMovieId is > 0)
        {
            return RedirectToAction(nameof(MovieDetail), new { id = request.TargetMovieId.Value });
        }

        return RedirectToAction(nameof(Movies));
    }

    [HttpPost("movies/sync-verified-catalog")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncVerifiedCatalog(CancellationToken ct)
    {
        var managerId = GetCurrentManagerId();
        var summary = await syncService.SyncVerifiedShowcaseCatalogAsync(
            publishAll: false,
            actorUserId: managerId,
            ct: ct);

        TempData["Success"] = $"Đã đồng bộ danh mục xác minh Wikidata/IMDb: tạo mới {summary.CreatedCount}, cập nhật {summary.UpdatedCount}, bảo toàn {summary.PreservedOverrideFieldsCount} trường đã khóa thủ công.";
        return RedirectToAction(nameof(Movies));
    }

    [HttpGet("movies/{id:long}")]
    public async Task<IActionResult> MovieDetail(long id, CancellationToken ct)
    {
        var movie = await queryService.GetMovieByIdAsync(id, ct);
        if (movie == null) return NotFound();

        var now = clock.UtcNow;
        var hasActiveShowtimes = movie.Showtimes.Any(s => s.Status != "CANCELLED" && s.EndUtc > now);

        var vm = new ManageMovieDetailViewModel
        {
            Movie = movie,
            Showtimes = movie.Showtimes.ToList(),
            IsTmdbConfigured = syncService.IsTmdbConfigured,
            HasActiveShowtimes = hasActiveShowtimes
        };

        return View(vm);
    }

    [HttpGet("movies/{id:long}/edit")]
    public async Task<IActionResult> MovieEdit(long id, CancellationToken ct)
    {
        var movie = await queryService.GetMovieByIdAsync(id, ct);
        if (movie == null) return NotFound();

        var now = clock.UtcNow;
        var hasActiveShowtimes = await db.Showtimes
            .AnyAsync(s => s.MovieId == id && s.Status != "CANCELLED" && s.EndUtc > now, ct);

        var vm = new ManageMovieFormViewModel
        {
            MovieId = movie.MovieId,
            Title = movie.Title,
            DurationMin = movie.DurationMin,
            GenreText = movie.GenreText,
            AgeLabel = movie.AgeLabel,
            Synopsis = movie.Synopsis,
            PosterUrl = movie.PosterUrl,
            Status = movie.Status,
            HasActiveShowtimes = hasActiveShowtimes,
            IsTmdbConfigured = syncService.IsTmdbConfigured,
            OriginalTitle = movie.OriginalTitle,
            ReleaseYear = movie.ReleaseYear,
            DirectorNames = movie.DirectorNames,
            OriginCountry = movie.OriginCountry,
            OriginalLanguage = movie.OriginalLanguage,
            WikidataQid = movie.WikidataQid,
            ImdbId = movie.ImdbId,
            TmdbId = movie.TmdbId,
            BackdropUrl = movie.BackdropUrl,
            OfficialTrailerYoutubeId = movie.OfficialTrailerYoutubeId,
            ArtworkSource = movie.ArtworkSource,
            MetadataSource = movie.MetadataSource,
            RatingSource = movie.RatingSource,
            RatingValue = movie.RatingValue,
            RatingVoteCount = movie.RatingVoteCount,
            ExternalSyncedUtc = movie.ExternalSyncedUtc,
            ManualOverrideMask = movie.ManualOverrideMask
        };

        return View(vm);
    }

    [HttpPost("movies/{id:long}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MovieEdit(
        long id, [FromForm] EditMovieRequest request, CancellationToken ct)
    {
        if (id != request.MovieId)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            var now = clock.UtcNow;
            var hasActiveShowtimes = await db.Showtimes
                .AnyAsync(s => s.MovieId == id && s.Status != "CANCELLED" && s.EndUtc > now, ct);
            var existingMovie = await queryService.GetMovieByIdAsync(id, ct);

            return View(MapEditRequestToFormVm(id, request, hasActiveShowtimes, existingMovie));
        }

        try
        {
            var managerId = GetCurrentManagerId();
            MovieProvenanceEditInput? provenance = null;
            if (request.ApplyOverrideLocks ||
                request.OriginalTitle != null ||
                request.ReleaseYear.HasValue ||
                request.DirectorNames != null ||
                request.WikidataQid != null ||
                request.ImdbId != null ||
                request.TmdbId.HasValue ||
                request.BackdropUrl != null ||
                request.OfficialTrailerYoutubeId != null)
            {
                provenance = new MovieProvenanceEditInput(
                    OriginalTitle: request.OriginalTitle,
                    ReleaseYear: request.ReleaseYear,
                    DirectorNames: request.DirectorNames,
                    OriginCountry: request.OriginCountry,
                    OriginalLanguage: request.OriginalLanguage,
                    WikidataQid: request.WikidataQid,
                    ImdbId: request.ImdbId,
                    TmdbId: request.TmdbId,
                    BackdropUrl: request.BackdropUrl,
                    OfficialTrailerYoutubeId: request.OfficialTrailerYoutubeId,
                    ApplyExplicitOverrideLocks: request.ApplyOverrideLocks,
                    ExplicitOverrideLocks: request.ManualOverrides);
            }

            await movieService.UpdateMovieAsync(
                managerId, id, request.Title, request.DurationMin,
                request.GenreText, request.AgeLabel, request.Synopsis,
                request.PosterUrl, ct, provenance);

            logger.LogInformation("Manager {ManagerId} updated movie {MovieId}", managerId, id);
            TempData["Success"] = $"Đã cập nhật thông tin phim '{request.Title}' thành công.";
            return RedirectToAction(nameof(MovieDetail), new { id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var now = clock.UtcNow;
            var hasActiveShowtimes = await db.Showtimes
                .AnyAsync(s => s.MovieId == id && s.Status != "CANCELLED" && s.EndUtc > now, ct);
            var existingMovie = await queryService.GetMovieByIdAsync(id, ct);

            return View(MapEditRequestToFormVm(id, request, hasActiveShowtimes, existingMovie));
        }
    }

    [HttpPost("movies/{id:long}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoviePublish(long id, CancellationToken ct)
    {
        try
        {
            var managerId = GetCurrentManagerId();
            await movieService.PublishMovieAsync(managerId, id, ct);
            logger.LogInformation("Manager {ManagerId} published movie {MovieId}", managerId, id);
            TempData["Success"] = "Đã xuất bản phim thành công! Phim đã sẵn sàng để lên lịch chiếu.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(MovieDetail), new { id });
    }

    [HttpPost("movies/{id:long}/archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MovieArchive(long id, CancellationToken ct)
    {
        try
        {
            var managerId = GetCurrentManagerId();
            await movieService.ArchiveMovieAsync(managerId, id, ct);
            logger.LogInformation("Manager {ManagerId} archived movie {MovieId}", managerId, id);
            TempData["Success"] = "Đã lưu trữ phim thành công. Không thể công bố thêm suất chiếu mới cho phim này.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(MovieDetail), new { id });
    }

    // =========================================================================
    // 3. SHOWTIME MANAGEMENT
    // =========================================================================

    [HttpGet("showtimes")]
    public async Task<IActionResult> Showtimes(
        [FromQuery] long? movieId,
        [FromQuery] long? roomId,
        [FromQuery] string? date,
        [FromQuery] string? status = "ALL",
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        DateTime? fromDateUtc = null;
        DateTime? toDateUtc = null;

        if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsedDate))
        {
            fromDateUtc = ConvertVnTimeToUtc(parsedDate.Date);
            toDateUtc = ConvertVnTimeToUtc(parsedDate.Date.AddDays(1).AddTicks(-1));
        }

        var result = await queryService.GetShowtimesAsync(
            movieId, roomId, fromDateUtc, toDateUtc, status, page, 15, ct);

        var movies = await db.Movies.AsNoTracking().OrderBy(m => m.Title).ToListAsync(ct);
        var rooms = await db.Rooms.AsNoTracking().OrderBy(r => r.RoomName).ToListAsync(ct);

        var vm = new ManageShowtimesViewModel
        {
            Items = result.Items,
            Movies = movies,
            Rooms = rooms,
            SelectedMovieId = movieId,
            SelectedRoomId = roomId,
            DateFilter = date,
            StatusFilter = status ?? "ALL",
            CurrentPage = result.Page,
            TotalPages = result.TotalPages,
            TotalCount = result.TotalCount
        };

        return View(vm);
    }

    [HttpGet("showtimes/create")]
    public async Task<IActionResult> ShowtimeCreate(
        [FromQuery] long? movieId = null,
        CancellationToken ct = default)
    {
        var publishedMovies = await db.Movies.AsNoTracking()
            .Where(m => m.Status == "PUBLISHED")
            .OrderBy(m => m.Title)
            .ToListAsync(ct);

        var activeRooms = await db.Rooms.AsNoTracking()
            .Where(r => r.Status == "ACTIVE")
            .Include(r => r.Seats)
            .OrderBy(r => r.RoomName)
            .ToListAsync(ct);

        var seatTypes = await db.SeatTypes.AsNoTracking()
            .Where(st => st.IsActive)
            .OrderBy(st => st.SeatTypeCode)
            .ToListAsync(ct);

        var defaultPrices = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in seatTypes)
        {
            defaultPrices[st.SeatTypeCode] = st.SeatTypeCode switch
            {
                "VIP" => 90000,
                "SWEETBOX" => 160000,
                _ => 75000
            };
        }

        var vm = new ManageShowtimeCreateViewModel
        {
            PublishedMovies = publishedMovies,
            ActiveRooms = activeRooms,
            SeatTypes = seatTypes,
            SelectedMovieId = movieId,
            Prices = defaultPrices,
            BufferMinutes = 15,
            StartTimeVn = DateTime.Now.AddHours(2)
        };

        return View(vm);
    }

    [HttpPost("showtimes/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShowtimeCreate(
        [FromForm] PublishShowtimeRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return await ReloadShowtimeCreateView(request, ct);
        }

        try
        {
            var managerId = GetCurrentManagerId();
            var startUtc = ConvertVnTimeToUtc(request.StartTimeVn);

            if (startUtc <= clock.UtcNow)
            {
                ModelState.AddModelError(nameof(request.StartTimeVn),
                    "Thời gian bắt đầu chiếu phải nằm trong tương lai.");
                return await ReloadShowtimeCreateView(request, ct);
            }

            var priceInputs = new List<SeatPriceInput>();
            foreach (var kvp in request.Prices)
            {
                if (kvp.Value <= 0)
                {
                    ModelState.AddModelError(string.Empty,
                        $"Giá vé cho loại ghế '{kvp.Key}' phải là số tiền dương lớn hơn 0.");
                    return await ReloadShowtimeCreateView(request, ct);
                }
                priceInputs.Add(new SeatPriceInput(kvp.Key.Trim().ToUpperInvariant(), kvp.Value));
            }

            if (priceInputs.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Vui lòng nhập giá vé cho các loại ghế trong phòng.");
                return await ReloadShowtimeCreateView(request, ct);
            }

            var showtimeId = await publishingService.PublishAsync(
                managerId, request.RoomId, request.MovieId,
                startUtc, request.BufferMinutes, priceInputs, ct);

            logger.LogInformation("Manager {ManagerId} published showtime {ShowtimeId} for movie {MovieId}", managerId, showtimeId, request.MovieId);
            TempData["Success"] = $"Công bố suất chiếu #{showtimeId} thành công!";
            return RedirectToAction(nameof(ShowtimeDetail), new { id = showtimeId });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadShowtimeCreateView(request, ct);
        }
    }

    private async Task<IActionResult> ReloadShowtimeCreateView(
        PublishShowtimeRequest request, CancellationToken ct)
    {
        var publishedMovies = await db.Movies.AsNoTracking()
            .Where(m => m.Status == "PUBLISHED").OrderBy(m => m.Title).ToListAsync(ct);
        var activeRooms = await db.Rooms.AsNoTracking()
            .Where(r => r.Status == "ACTIVE").Include(r => r.Seats).OrderBy(r => r.RoomName).ToListAsync(ct);
        var seatTypes = await db.SeatTypes.AsNoTracking()
            .Where(st => st.IsActive).OrderBy(st => st.SeatTypeCode).ToListAsync(ct);

        var vm = new ManageShowtimeCreateViewModel
        {
            PublishedMovies = publishedMovies,
            ActiveRooms = activeRooms,
            SeatTypes = seatTypes,
            SelectedMovieId = request.MovieId,
            SelectedRoomId = request.RoomId,
            StartTimeVn = request.StartTimeVn,
            BufferMinutes = request.BufferMinutes,
            Prices = request.Prices
        };

        return View(vm);
    }

    [HttpGet("showtimes/{id:long}")]
    public async Task<IActionResult> ShowtimeDetail(long id, CancellationToken ct)
    {
        var details = await queryService.GetShowtimeDetailsAsync(id, ct);
        if (details == null) return NotFound();

        return View(new ManageShowtimeDetailViewModel { Details = details });
    }

    [HttpPost("showtimes/{id:long}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShowtimeClose(long id, CancellationToken ct)
    {
        try
        {
            var managerId = GetCurrentManagerId();
            await queryService.CloseShowtimeAsync(managerId, id, ct);
            logger.LogInformation("Manager {ManagerId} closed showtime {ShowtimeId}", managerId, id);
            TempData["Success"] = $"Đã đóng suất chiếu #{id} thành công.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(ShowtimeDetail), new { id });
    }

    [HttpPost("showtimes/{id:long}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShowtimeCancel(long id, CancellationToken ct)
    {
        try
        {
            var managerId = GetCurrentManagerId();
            await queryService.CancelShowtimeAsync(managerId, id, ct);
            logger.LogInformation("Manager {ManagerId} cancelled showtime {ShowtimeId}", managerId, id);
            TempData["Success"] = $"Đã hủy suất chiếu #{id} thành công. Toàn bộ ghế giữ đã được hoàn trả.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(ShowtimeDetail), new { id });
    }

    // =========================================================================
    // 4. ROOMS & SEATS REFERENCE
    // =========================================================================

    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms(CancellationToken ct)
    {
        var rooms = await queryService.GetRoomsAsync(ct);
        return View(new ManageRoomsViewModel { Rooms = rooms });
    }

    [HttpGet("rooms/{id:long}")]
    public async Task<IActionResult> RoomDetail(long id, CancellationToken ct)
    {
        var room = await queryService.GetRoomDetailsWithSeatLayoutAsync(id, ct);
        if (room == null) return NotFound();

        return View(new ManageRoomDetailViewModel { Room = room });
    }
}
