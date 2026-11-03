using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Tests.IntegrationTests;
using Xunit;

namespace BetaGiaiPhong.Tests.WebTests;

[Collection("DatabaseCollection")]
public sealed class ManagementWebTests(TestDatabaseFixture fixture) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory = new(fixture);

    private HttpClient CreateNewClient()
    {
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    private static async Task<(string Cookie, string Token)> ExtractAntiforgeryTokenAsync(HttpClient client, string url, string? authCookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(authCookie))
        {
            request.Headers.Add("Cookie", authCookie);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var setCookieHeader = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(".BetaGiaiPhong.Antiforgery"));
        Assert.NotNull(setCookieHeader);

        var cookie = setCookieHeader.Split(';')[0];
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)""\s+name=""__RequestVerificationToken""");
        }
        Assert.True(match.Success, "Could not extract __RequestVerificationToken from HTML");

        var token = match.Groups[1].Value;
        return (cookie, token);
    }

    private async Task<(string AuthCookie, string AfCookie, string XsrfToken)> LoginAndGetTokensAsync(
        HttpClient client, string email, string password = "DemoPassword123!")
    {
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Request.Email"] = email,
            ["Request.Password"] = password
        };

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(form)
        };
        loginReq.Headers.Add("Cookie", afCookie);

        var loginResp = await client.SendAsync(loginReq);
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        var authCookie = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(".BetaGiaiPhong.Auth")).Split(';')[0];

        // Refresh tokens with authenticated identity
        var getReq = new HttpRequestMessage(HttpMethod.Get, "/");
        getReq.Headers.Add("Cookie", authCookie);
        var getResp = await client.SendAsync(getReq);

        var newAfCookieHeader = getResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(".BetaGiaiPhong.Antiforgery"));
        var newAfCookie = newAfCookieHeader?.Split(';')[0] ?? afCookie;

        var html = await getResp.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        var token = match.Success ? match.Groups[1].Value : afToken;

        return (authCookie, newAfCookie, token);
    }

    [Fact]
    public async Task TC_MGMT_01_AnonymousUser_RedirectsToLogin()
    {
        var client = CreateNewClient();
        var routes = new[] { "/management/reports", "/management/movies", "/management/showtimes", "/management/rooms" };

        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/account/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TC_MGMT_02_CustomerAndStaff_ReturnsForbidden()
    {
        var client = CreateNewClient();

        // Customer attempt
        var (custAuth, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");
        var custReq = new HttpRequestMessage(HttpMethod.Get, "/management/reports");
        custReq.Headers.Add("Cookie", custAuth);
        var custResp = await client.SendAsync(custReq);
        Assert.True(
            custResp.StatusCode == HttpStatusCode.Forbidden ||
            (custResp.StatusCode == HttpStatusCode.Redirect && custResp.Headers.Location?.ToString().Contains("access-denied") == true),
            $"Expected 403 or redirect to access-denied, got: {custResp.StatusCode}");

        // Staff attempt
        var (staffAuth, _, _) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");
        var staffReq = new HttpRequestMessage(HttpMethod.Get, "/management/reports");
        staffReq.Headers.Add("Cookie", staffAuth);
        var staffResp = await client.SendAsync(staffReq);
        Assert.True(
            staffResp.StatusCode == HttpStatusCode.Forbidden ||
            (staffResp.StatusCode == HttpStatusCode.Redirect && staffResp.Headers.Location?.ToString().Contains("access-denied") == true),
            $"Expected 403 or redirect to access-denied, got: {staffResp.StatusCode}");
    }

    [Fact]
    public async Task TC_MGMT_03_Manager_CanAccessAllManagementRoutes()
    {
        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        var routes = new[] { "/management/reports", "/management/movies", "/management/showtimes", "/management/rooms" };

        foreach (var route in routes)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, route);
            req.Headers.Add("Cookie", mgrAuth);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact]
    public async Task TC_MGMT_04_DisabledManager_RejectedByPrincipalValidation()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.SingleAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");

        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        try
        {
            // Lock/disable the manager in database
            managerUser.Status = "DISABLED";
            await db.SaveChangesAsync();

            // Next request should trigger OnValidatePrincipal and reject the cookie
            var req = new HttpRequestMessage(HttpMethod.Get, "/management/reports");
            req.Headers.Add("Cookie", mgrAuth);
            var resp = await client.SendAsync(req);

            Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
            Assert.Contains("/account/login", resp.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // Restore manager status for other tests
            managerUser.Status = "ACTIVE";
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TC_MGMT_05_MovieCreate_RequiresAntiforgery()
    {
        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        var form = new Dictionary<string, string>
        {
            ["Title"] = "Test Movie No CSRF",
            ["DurationMin"] = "120"
        };

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/management/movies/create")
        {
            Content = new FormUrlEncodedContent(form)
        };
        postReq.Headers.Add("Cookie", mgrAuth);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_MGMT_06_MovieCreate_Valid_CreatesDraftMovieAndAuditLog()
    {
        var client = CreateNewClient();
        var (mgrAuth, afCookie, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        var (formAfCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/management/movies/create", mgrAuth);

        var title = $"Movie_{Guid.NewGuid():N}";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Title"] = title,
            ["DurationMin"] = "115",
            ["GenreText"] = "Hành động, Viễn tưởng",
            ["AgeLabel"] = "T16",
            ["Synopsis"] = "Tóm tắt phim thử nghiệm tự động.",
            ["PosterUrl"] = "https://images.unsplash.com/photo-test.jpg"
        };

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/management/movies/create")
        {
            Content = new FormUrlEncodedContent(form)
        };
        postReq.Headers.Add("Cookie", $"{mgrAuth}; {formAfCookie}");

        var response = await client.SendAsync(postReq);
        var respContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Failed to redirect, got {response.StatusCode}. Content: {respContent}");

        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.SingleOrDefaultAsync(m => m.Title == title);
        Assert.NotNull(movie);
        Assert.Equal("DRAFT", movie.Status);
        Assert.Equal(115, movie.DurationMin);
        Assert.Equal("T16", movie.AgeLabel);

        // Verify audit log
        var audit = await db.AuditLogs
            .Where(a => a.EventType == "MOVIE_CREATED" && a.EntityType == "Movie" && a.EntityKey == movie.MovieId.ToString())
            .SingleOrDefaultAsync();
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task TC_MGMT_07_MoviePublishAndArchive_LifecycleTransitionsCorrectly()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.SingleAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");

        // 1. Create a draft movie directly
        var movie = new Movie
        {
            Title = $"Lifecycle_{Guid.NewGuid():N}",
            DurationMin = 100,
            AgeLabel = "P",
            Status = "DRAFT"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        // 2. Publish
        var (pubAfCookie, pubAfToken) = await ExtractAntiforgeryTokenAsync(client, $"/management/movies/{movie.MovieId}", mgrAuth);
        var pubReq = new HttpRequestMessage(HttpMethod.Post, $"/management/movies/{movie.MovieId}/publish")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = pubAfToken
            })
        };
        pubReq.Headers.Add("Cookie", $"{mgrAuth}; {pubAfCookie}");

        var pubResp = await client.SendAsync(pubReq);
        Assert.Equal(HttpStatusCode.Redirect, pubResp.StatusCode);

        await db.Entry(movie).ReloadAsync();
        Assert.Equal("PUBLISHED", movie.Status);

        var pubAudit = await db.AuditLogs
            .Where(a => a.EventType == "MOVIE_PUBLISHED" && a.EntityKey == movie.MovieId.ToString())
            .SingleOrDefaultAsync();
        Assert.NotNull(pubAudit);

        // 3. Archive
        var (arcAfCookie, arcAfToken) = await ExtractAntiforgeryTokenAsync(client, $"/management/movies/{movie.MovieId}", mgrAuth);
        var arcReq = new HttpRequestMessage(HttpMethod.Post, $"/management/movies/{movie.MovieId}/archive")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = arcAfToken
            })
        };
        arcReq.Headers.Add("Cookie", $"{mgrAuth}; {arcAfCookie}");

        var arcResp = await client.SendAsync(arcReq);
        Assert.Equal(HttpStatusCode.Redirect, arcResp.StatusCode);

        await db.Entry(movie).ReloadAsync();
        Assert.Equal("ARCHIVED", movie.Status);

        var arcAudit = await db.AuditLogs
            .Where(a => a.EventType == "MOVIE_ARCHIVED" && a.EntityKey == movie.MovieId.ToString())
            .SingleOrDefaultAsync();
        Assert.NotNull(arcAudit);
    }

    [Fact]
    public async Task TC_MGMT_08_MovieEdit_DurationLockedWhenActiveShowtimesExist()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.SingleAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync(r => r.Status == "ACTIVE");

        var movie = new Movie
        {
            Title = $"LockedMovie_{Guid.NewGuid():N}",
            DurationMin = 120,
            AgeLabel = "T13",
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        // Create an active future showtime
        var startUtc = DateTime.UtcNow.AddDays(2);
        var endUtc = startUtc.AddMinutes(120);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        // Try to change duration via MovieManagementService directly
        var clock = new SystemClock();
        var service = new MovieManagementService(db, clock);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateMovieAsync(managerUser.UserId, movie.MovieId, movie.Title, 90, null, "T13", null, null));

        Assert.Equal("CANNOT_CHANGE_DURATION", ex.Code);
    }

    [Fact]
    public async Task TC_MGMT_09_ShowtimeCreate_Valid_CreatesOpenShowtimeWithSeatsAndPrices()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.SingleAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await db.Rooms.Include(r => r.Seats).FirstAsync(r => r.Status == "ACTIVE" && r.Seats.Any());

        var movie = new Movie
        {
            Title = $"PubShow_{Guid.NewGuid():N}",
            DurationMin = 90,
            AgeLabel = "P",
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        var (formAfCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/management/showtimes/create", mgrAuth);

        var roomSeatTypes = await db.Seats
            .Where(s => s.RoomId == room.RoomId && s.IsActive)
            .Select(s => s.SeatTypeCode)
            .Distinct()
            .ToListAsync();

        var maxEndUtc = await db.Showtimes
            .Where(s => s.RoomId == room.RoomId && s.Status != "CANCELLED")
            .MaxAsync(s => (DateTime?)s.EndUtc);

        var nextStartUtc = (maxEndUtc ?? DateTime.UtcNow).AddDays(2).Date.AddHours(14);
        var startTimeVn = nextStartUtc.AddHours(7).ToString("yyyy-MM-ddTHH:mm");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["MovieId"] = movie.MovieId.ToString(),
            ["RoomId"] = room.RoomId.ToString(),
            ["StartTimeVn"] = startTimeVn,
            ["BufferMinutes"] = "15"
        };
        foreach (var stCode in roomSeatTypes)
        {
            form[$"Prices[{stCode}]"] = "75000";
        }

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/management/showtimes/create")
        {
            Content = new FormUrlEncodedContent(form)
        };
        postReq.Headers.Add("Cookie", $"{mgrAuth}; {formAfCookie}");

        var response = await client.SendAsync(postReq);
        var respContent = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Failed to redirect, got {response.StatusCode}. Content: {respContent}");

        // Verify in DB
        var createdShowtime = await db.Showtimes
            .Include(s => s.ShowtimeSeats)
            .Include(s => s.SeatPrices)
            .OrderByDescending(s => s.ShowtimeId)
            .FirstOrDefaultAsync(s => s.MovieId == movie.MovieId);

        Assert.NotNull(createdShowtime);
        Assert.Equal("ON_SALE", createdShowtime.Status);
        Assert.True(createdShowtime.ShowtimeSeats.Count > 0);
        Assert.True(createdShowtime.SeatPrices.Count >= 2);
    }

    [Fact]
    public async Task TC_MGMT_10_ShowtimeCloseAndCancel_GuardedCorrectly()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.SingleAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await db.Rooms.Include(r => r.Seats).FirstAsync(r => r.Status == "ACTIVE");

        var movie = new Movie
        {
            Title = $"CloseCancel_{Guid.NewGuid():N}",
            DurationMin = 100,
            AgeLabel = "P",
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(5);
        var endUtc = startUtc.AddMinutes(100);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 20,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        // 1. Close showtime
        var (closeCookie, closeToken) = await ExtractAntiforgeryTokenAsync(client, $"/management/showtimes/{showtime.ShowtimeId}", mgrAuth);
        var closeReq = new HttpRequestMessage(HttpMethod.Post, $"/management/showtimes/{showtime.ShowtimeId}/close")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = closeToken })
        };
        closeReq.Headers.Add("Cookie", $"{mgrAuth}; {closeCookie}");

        var closeResp = await client.SendAsync(closeReq);
        Assert.Equal(HttpStatusCode.Redirect, closeResp.StatusCode);

        await db.Entry(showtime).ReloadAsync();
        Assert.Equal("CLOSED", showtime.Status);

        // 2. Cancel showtime (0 tickets sold -> allowed)
        var (cancelCookie, cancelToken) = await ExtractAntiforgeryTokenAsync(client, $"/management/showtimes/{showtime.ShowtimeId}", mgrAuth);
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/management/showtimes/{showtime.ShowtimeId}/cancel")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = cancelToken })
        };
        cancelReq.Headers.Add("Cookie", $"{mgrAuth}; {cancelCookie}");

        var cancelResp = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.Redirect, cancelResp.StatusCode);

        await db.Entry(showtime).ReloadAsync();
        Assert.Equal("CANCELLED", showtime.Status);
    }

    [Fact]
    public async Task TC_MGMT_11_RoomReference_RendersListAndDetail()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var room = await db.Rooms.FirstAsync();

        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        // List
        var listReq = new HttpRequestMessage(HttpMethod.Get, "/management/rooms");
        listReq.Headers.Add("Cookie", mgrAuth);
        var listResp = await client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listHtml = await listResp.Content.ReadAsStringAsync();
        Assert.Contains(room.RoomName, listHtml);

        // Detail
        var detailReq = new HttpRequestMessage(HttpMethod.Get, $"/management/rooms/{room.RoomId}");
        detailReq.Headers.Add("Cookie", mgrAuth);
        var detailResp = await client.SendAsync(detailReq);
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
        var detailHtml = await detailResp.Content.ReadAsStringAsync();
        Assert.Contains(room.RoomName, detailHtml);
        Assert.Contains("Sơ đồ bố trí ghế vật lý", detailHtml);
    }

    [Fact]
    public async Task TC_MGMT_12_SalesDashboard_ReturnsOkAndRendersKPIs()
    {
        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Get, "/management/reports");
        req.Headers.Add("Cookie", mgrAuth);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Báo cáo Vận hành & Doanh thu", html);
        Assert.Contains("Tổng Doanh thu Hợp lệ", html);
        Assert.Contains("Tổng Vé Đã Bán", html);
        Assert.Contains("Tỷ lệ Lấp đầy Bình quân", html);
    }

    [Fact]
    public async Task TC_MGMT_13_DirectReservation_OnArchivedMovie_IsRejected()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();

        var leftovers = await db.Showtimes
            .Where(s => s.Movie != null && s.Movie.Title.StartsWith("Archived Test Movie") && s.Status == "ON_SALE")
            .ToListAsync();
        foreach (var l in leftovers) l.Status = "CLOSED";
        if (leftovers.Count > 0) await db.SaveChangesAsync();

        var movie = new Movie
        {
            Title = "Archived Test Movie " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "ARCHIVED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(20);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seat = await db.Seats.FirstAsync(s => s.RoomId == room.RoomId && s.IsActive);
        db.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = SeatStates.Available
        });
        db.SeatPrices.Add(new SeatPrice
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatTypeCode = seat.SeatTypeCode,
            AmountVnd = 75000,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();


        try
        {
            var reservationService = new SeatReservationService(db, new SystemClock());
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                reservationService.ReserveAsync(customerUser.UserId, showtime.ShowtimeId, new[] { seat.SeatId }));

            Assert.Equal("MOVIE_NOT_PUBLISHED", ex.Code);
        }
        finally
        {
            showtime.Status = "CLOSED";
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TC_MGMT_14_ArchiveMovie_WithActiveShowtimes_IsRejected()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Active Showtime Movie " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 110,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(25);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(110),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var movieService = new MovieManagementService(db, new SystemClock());
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            movieService.ArchiveMovieAsync(managerUser.UserId, movie.MovieId));

        Assert.Equal("CANNOT_ARCHIVE_ACTIVE_SHOWTIMES", ex.Code);

        await db.Entry(movie).ReloadAsync();
        Assert.Equal("PUBLISHED", movie.Status);
    }

    [Fact]
    public async Task TC_MGMT_15_ArchiveMovie_WithOnlyCancelledOrPastShowtimes_Succeeds()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Past/Cancelled Movie " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 90,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        // 1 past showtime
        var pastStart = DateTime.UtcNow.AddDays(-2);
        var pastShowtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = pastStart,
            EndUtc = pastStart.AddMinutes(90),
            BufferMinutes = 15,
            Status = "CLOSED",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow.AddDays(-3)
        };
        // 1 future cancelled showtime
        var futureStart = DateTime.UtcNow.AddDays(30);
        var cancelledShowtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = futureStart,
            EndUtc = futureStart.AddMinutes(90),
            BufferMinutes = 15,
            Status = "CANCELLED",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.AddRange(pastShowtime, cancelledShowtime);
        await db.SaveChangesAsync();

        var movieService = new MovieManagementService(db, new SystemClock());
        await movieService.ArchiveMovieAsync(managerUser.UserId, movie.MovieId);

        await db.Entry(movie).ReloadAsync();
        Assert.Equal("ARCHIVED", movie.Status);

        var audit = await db.AuditLogs
            .AnyAsync(a => a.EventType == "MOVIE_ARCHIVED" && a.EntityKey == movie.MovieId.ToString());
        Assert.True(audit);
    }

    [Fact]
    public async Task TC_MGMT_16_EditArchivedMovie_IsRejected()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");

        var movie = new Movie
        {
            Title = "Archived For Edit " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "ARCHIVED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var movieService = new MovieManagementService(db, new SystemClock());
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            movieService.UpdateMovieAsync(managerUser.UserId, movie.MovieId, "New Title", 120, null, "P", null, null));

        Assert.Equal("INVALID_STATUS", ex.Code);
    }

    [Fact]
    public async Task TC_MGMT_17_MovieValidation_RejectsInvalidAgeLabel_AndDangerousPosterUrl()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var movieService = new MovieManagementService(db, new SystemClock());

        // Invalid AgeLabel
        var exAge = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            movieService.CreateMovieAsync(managerUser.UserId, "Title", 100, "Action", "PG-13", null, null));
        Assert.Equal("INVALID_AGE_LABEL", exAge.Code);

        // Dangerous PosterUrl: javascript
        var exJs = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            movieService.CreateMovieAsync(managerUser.UserId, "Title", 100, "Action", "T18", null, "javascript:alert(1)"));
        Assert.Equal("INVALID_POSTER_URL", exJs.Code);

        // Protocol-relative PosterUrl: //
        var exProto = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            movieService.CreateMovieAsync(managerUser.UserId, "Title", 100, "Action", "T18", null, "//evil.com/x.jpg"));
        Assert.Equal("INVALID_POSTER_URL", exProto.Code);

        // Backslash PosterUrl
        var exBs = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            movieService.CreateMovieAsync(managerUser.UserId, "Title", 100, "Action", "T18", null, @"\evil.com\x.jpg"));
        Assert.Equal("INVALID_POSTER_URL", exBs.Code);

        // Valid AgeLabel and PosterUrl
        var id = await movieService.CreateMovieAsync(managerUser.UserId, "Valid Movie " + Guid.NewGuid().ToString("N")[..6],
            100, "Drama", "T16", "Synopsis", "/images/posters/valid.jpg");
        Assert.True(id > 0);
    }

    [Fact]
    public async Task TC_MGMT_18_CancelShowtime_FinalizesPendingOrders_AndReleasesHeldSeats()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Cancel Showtime Test " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(40);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seats = await db.Seats.Where(s => s.RoomId == room.RoomId && s.IsActive).Take(2).ToListAsync();
        foreach (var s in seats)
        {
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = s.SeatId,
                RoomId = room.RoomId,
                State = SeatStates.Available
            });
        }
        foreach (var st in seats.Select(s => s.SeatTypeCode).Distinct())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st,
                AmountVnd = 80000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        // Customer reserves 2 seats -> Order is PENDING_PAYMENT
        var reservationService = new SeatReservationService(db, new SystemClock());
        var resResult = await reservationService.ReserveAsync(customerUser.UserId, showtime.ShowtimeId, seats.Select(s => s.SeatId).ToList());

        var orderBefore = await db.Orders.SingleAsync(o => o.OrderId == resResult.OrderId);
        Assert.Equal(OrderStates.Pending, orderBefore.Status);

        // Manager cancels showtime
        var queryService = new ManagementQueryService(db, new SystemClock());
        await queryService.CancelShowtimeAsync(managerUser.UserId, showtime.ShowtimeId);

        // Assert Order is now CANCELLED
        var orderAfter = await db.Orders.SingleAsync(o => o.OrderId == resResult.OrderId);
        Assert.Equal(OrderStates.Cancelled, orderAfter.Status);

        // Assert held seats are released to AVAILABLE
        var heldCount = await db.ShowtimeSeats
            .CountAsync(ss => ss.ShowtimeId == showtime.ShowtimeId && ss.State == SeatStates.Held);
        Assert.Equal(0, heldCount);

        var availableCount = await db.ShowtimeSeats
            .CountAsync(ss => ss.ShowtimeId == showtime.ShowtimeId && ss.State == SeatStates.Available);
        Assert.Equal(2, availableCount);

        // Assert AuditLog entries exist
        var orderAudit = await db.AuditLogs
            .AnyAsync(a => a.EventType == "ORDER_CANCELLED" && a.EntityKey == resResult.OrderId.ToString());
        Assert.True(orderAudit);

        var showtimeAudit = await db.AuditLogs
            .AnyAsync(a => a.EventType == "SHOWTIME_CANCELLED" && a.EntityKey == showtime.ShowtimeId.ToString());
        Assert.True(showtimeAudit);
    }

    [Fact]
    public async Task TC_MGMT_19_PaymentAttempt_ForCancelledShowtimeOrder_IsRejected()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER2@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Cancel Payment Test " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(42);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seat = await db.Seats.FirstAsync(s => s.RoomId == room.RoomId && s.IsActive);
        db.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = SeatStates.Available
        });
        db.SeatPrices.Add(new SeatPrice
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatTypeCode = seat.SeatTypeCode,
            AmountVnd = 70000,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reservationService = new SeatReservationService(db, new SystemClock());
        var resResult = await reservationService.ReserveAsync(customerUser.UserId, showtime.ShowtimeId, new[] { seat.SeatId });

        // Cancel showtime
        var queryService = new ManagementQueryService(db, new SystemClock());
        await queryService.CancelShowtimeAsync(managerUser.UserId, showtime.ShowtimeId);

        // Attempt to create payment on the order
        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            paymentService.CreateMockAttemptAsync(customerUser.UserId, resResult.OrderId, "ATTEMPT-" + Guid.NewGuid().ToString("N")));

        Assert.Equal("ORDER_UNAVAILABLE", ex.Code);
    }

    [Fact]
    public async Task TC_MGMT_20_LateWebhook_ForCancelledShowtimeOrder_SetsNeedsReview_NoTicketsIssued()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Late Webhook Cancel Test " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(45);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seat = await db.Seats.FirstAsync(s => s.RoomId == room.RoomId && s.IsActive);
        db.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = SeatStates.Available
        });
        db.SeatPrices.Add(new SeatPrice
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatTypeCode = seat.SeatTypeCode,
            AmountVnd = 75000,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reservationService = new SeatReservationService(db, new SystemClock());
        var resResult = await reservationService.ReserveAsync(customerUser.UserId, showtime.ShowtimeId, new[] { seat.SeatId });

        // Create attempt before cancellation
        var attemptKey = "ATTEMPT-" + Guid.NewGuid().ToString("N");
        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        await paymentService.CreateMockAttemptAsync(customerUser.UserId, resResult.OrderId, attemptKey);

        // Cancel showtime
        var queryService = new ManagementQueryService(db, new SystemClock());
        await queryService.CancelShowtimeAsync(managerUser.UserId, showtime.ShowtimeId);

        // Webhook SUCCESS arrives
        var callback = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey,
            "MOCK-TXN-" + Guid.NewGuid().ToString("N"), resResult.OrderCode, resResult.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(callback, MockPaymentSecurity.SignForDevelopment(callback));

        var result = await paymentService.ApplyVerifiedMockAsync(verified);

        Assert.Equal("NEEDS_REVIEW", result.Decision);
        Assert.Equal(OrderStates.Cancelled, result.OrderStatus);

        var ticketsExist = await db.Tickets.AnyAsync(t => t.OrderId == resResult.OrderId);
        Assert.False(ticketsExist);

        var payment = await db.Payments.SingleAsync(p => p.OrderId == resResult.OrderId);
        Assert.Equal("NEEDS_REVIEW", payment.Status);
    }

    [Fact]
    public async Task TC_MGMT_21_CancelShowtime_WithPaidOrderOrTickets_IsRejected()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Paid Guard Test " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(48);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seat = await db.Seats.FirstAsync(s => s.RoomId == room.RoomId && s.IsActive);
        var order = new BookingOrder
        {
            OrderCode = "BGP-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime.ShowtimeId,
            Status = OrderStates.Paid,
            SeatCount = 1,
            SubtotalVnd = 75000,
            TotalVnd = 75000,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(5),
            PaidUtc = DateTime.UtcNow
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        db.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = SeatStates.Sold,
            OwnerOrderId = order.OrderId
        });
        db.OrderSeats.Add(new OrderSeat
        {
            OrderId = order.OrderId,
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            SeatTypeSnapshot = seat.SeatTypeCode,
            UnitPriceVnd = 75000
        });
        await db.SaveChangesAsync();

        var qrHash = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(qrHash);
        var qrCipher = new byte[48];
        System.Security.Cryptography.RandomNumberGenerator.Fill(qrCipher);

        db.Tickets.Add(new Ticket
        {
            TicketCode = "TKT-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            OrderId = order.OrderId,
            ShowtimeId = showtime.ShowtimeId,
            SeatId = seat.SeatId,
            Status = "ISSUED",
            QrTokenHash = qrHash,
            QrTokenCipher = qrCipher,
            QrKeyId = "k1",
            IssuedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var queryService = new ManagementQueryService(db, new SystemClock());
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            queryService.CancelShowtimeAsync(managerUser.UserId, showtime.ShowtimeId));

        Assert.Equal("CANNOT_CANCEL_SHOWTIME", ex.Code);

        await db.Entry(showtime).ReloadAsync();
        Assert.Equal("ON_SALE", showtime.Status);
    }

    [Fact]
    public async Task TC_MGMT_22_CloseShowtime_AfterCancelled_IsRejected()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();
        var movie = await db.Movies.FirstAsync();

        var startUtc = DateTime.UtcNow.AddDays(50);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(90),
            BufferMinutes = 15,
            Status = "CANCELLED",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var queryService = new ManagementQueryService(db, new SystemClock());
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            queryService.CloseShowtimeAsync(managerUser.UserId, showtime.ShowtimeId));

        Assert.Equal("INVALID_STATE_TRANSITION", ex.Code);
    }

    [Fact]
    public async Task TC_MGMT_23_CloseShowtime_AllowsExistingHeldOrderToPayBeforeExpiry()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var room = await db.Rooms.FirstAsync();


        var movie = new Movie
        {
            Title = "Close Grace Period " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "PUBLISHED"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();

        var startUtc = DateTime.UtcNow.AddDays(55);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seats = await db.Seats.Where(s => s.RoomId == room.RoomId && s.IsActive).Take(2).ToListAsync();
        foreach (var s in seats)
        {
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = s.SeatId,
                RoomId = room.RoomId,
                State = SeatStates.Available
            });
        }
        foreach (var st in seats.Select(s => s.SeatTypeCode).Distinct())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st,
                AmountVnd = 80000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        // 1. Customer reserves 1 seat while ON_SALE
        var reservationService = new SeatReservationService(db, new SystemClock());
        var resResult = await reservationService.ReserveAsync(customerUser.UserId, showtime.ShowtimeId, new[] { seats[0].SeatId });

        var attemptKey = "ATTEMPT-" + Guid.NewGuid().ToString("N");
        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        await paymentService.CreateMockAttemptAsync(customerUser.UserId, resResult.OrderId, attemptKey);

        // 2. Manager closes showtime
        var queryService = new ManagementQueryService(db, new SystemClock());
        await queryService.CloseShowtimeAsync(managerUser.UserId, showtime.ShowtimeId);

        // 3. New reservation on the remaining seat MUST be rejected (since showtime is CLOSED)
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            reservationService.ReserveAsync(customerUser.UserId, showtime.ShowtimeId, new[] { seats[1].SeatId }));
        Assert.Equal("NOT_ON_SALE", ex.Code);

        // 4. In-flight checkout can complete successfully
        var callback = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey,
            "MOCK-TXN-" + Guid.NewGuid().ToString("N"), resResult.OrderCode, resResult.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(callback, MockPaymentSecurity.SignForDevelopment(callback));
        var payResult = await paymentService.ApplyVerifiedMockAsync(verified);

        Assert.Equal("APPLIED", payResult.Decision);
        Assert.Equal(OrderStates.Paid, payResult.OrderStatus);

        var ticketIssued = await db.Tickets.AnyAsync(t => t.OrderId == resResult.OrderId);
        Assert.True(ticketIssued);
    }

    [Fact]
    public async Task TC_MGMT_24_CookieRevocation_StaffRoleRemoved_RejectsStaffCheckIn()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var staffRole = await db.Roles.SingleAsync(r => r.RoleCode == "STAFF");

        // Create temporary staff user so other tests aren't affected
        var staffEmail = $"tempstaff_{Guid.NewGuid():N}@demo.invalid";
        var tempStaff = new AppUser
        {
            Email = staffEmail,
            NormalizedEmail = staffEmail.ToUpperInvariant(),
            DisplayName = "Temp Staff",
            PasswordHash = new PasswordHasher<AppUser>().HashPassword(null!, "DemoPassword123!"),
            Status = "ACTIVE",
            CreatedUtc = DateTime.UtcNow
        };
        db.Users.Add(tempStaff);
        await db.SaveChangesAsync();

        var userRole = new UserRole { UserId = tempStaff.UserId, RoleId = staffRole.RoleId };
        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (staffAuth, _, _) = await LoginAndGetTokensAsync(client, tempStaff.Email);

        // Verify initial access works
        var okReq = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in");
        okReq.Headers.Add("Cookie", staffAuth);
        var okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        // Remove STAFF role from DB
        db.UserRoles.Remove(userRole);
        await db.SaveChangesAsync();

        // Now request with the existing cookie -> Should be rejected and redirected to access-denied
        var rejectedReq = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in");
        rejectedReq.Headers.Add("Cookie", staffAuth);
        var rejectedResp = await client.SendAsync(rejectedReq);

        Assert.True(rejectedResp.StatusCode == HttpStatusCode.Redirect ||
                    rejectedResp.StatusCode == HttpStatusCode.Forbidden ||
                    rejectedResp.StatusCode == HttpStatusCode.Unauthorized);

        if (rejectedResp.StatusCode == HttpStatusCode.Redirect)
        {
            var location = rejectedResp.Headers.Location?.ToString() ?? string.Empty;
            Assert.True(location.Contains("access-denied") || location.Contains("login"));
        }
    }

    [Fact]
    public async Task TC_MGMT_25_CookieRevocation_CustomerRoleRemoved_RejectsCustomerEndpoints()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var customerRole = await db.Roles.SingleAsync(r => r.RoleCode == "CUSTOMER");

        var custEmail = $"tempcust_{Guid.NewGuid():N}@demo.invalid";
        var tempCust = new AppUser
        {
            Email = custEmail,
            NormalizedEmail = custEmail.ToUpperInvariant(),
            DisplayName = "Temp Customer",
            PasswordHash = new PasswordHasher<AppUser>().HashPassword(null!, "DemoPassword123!"),
            Status = "ACTIVE",
            CreatedUtc = DateTime.UtcNow
        };
        db.Users.Add(tempCust);
        await db.SaveChangesAsync();

        var userRole = new UserRole { UserId = tempCust.UserId, RoleId = customerRole.RoleId };
        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (custAuth, _, _) = await LoginAndGetTokensAsync(client, tempCust.Email);

        // Verify initial access works
        var okReq = new HttpRequestMessage(HttpMethod.Get, "/orders");
        okReq.Headers.Add("Cookie", custAuth);
        var okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        // Remove CUSTOMER role from DB
        db.UserRoles.Remove(userRole);
        await db.SaveChangesAsync();

        // Access protected customer endpoint -> Should be rejected
        var req = new HttpRequestMessage(HttpMethod.Get, "/orders");
        req.Headers.Add("Cookie", custAuth);
        var resp = await client.SendAsync(req);

        Assert.True(resp.StatusCode == HttpStatusCode.Redirect ||
                    resp.StatusCode == HttpStatusCode.Forbidden ||
                    resp.StatusCode == HttpStatusCode.Unauthorized);

        if (resp.StatusCode == HttpStatusCode.Redirect)
        {
            var location = resp.Headers.Location?.ToString() ?? string.Empty;
            Assert.True(location.Contains("access-denied") || location.Contains("login"));
        }
    }

    [Fact]
    public async Task TC_MGMT_26_CookieRevocation_ManagerRoleRemoved_RejectsManagerPortal()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var mgrRole = await db.Roles.SingleAsync(r => r.RoleCode == "MANAGER");

        var mgrEmail = $"tempmgr_{Guid.NewGuid():N}@demo.invalid";
        var tempMgr = new AppUser
        {
            Email = mgrEmail,
            NormalizedEmail = mgrEmail.ToUpperInvariant(),
            DisplayName = "Temp Manager",
            PasswordHash = new PasswordHasher<AppUser>().HashPassword(null!, "DemoPassword123!"),
            Status = "ACTIVE",
            CreatedUtc = DateTime.UtcNow
        };
        db.Users.Add(tempMgr);
        await db.SaveChangesAsync();

        var userRole = new UserRole { UserId = tempMgr.UserId, RoleId = mgrRole.RoleId };
        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, tempMgr.Email);

        // Verify initial access
        var okReq = new HttpRequestMessage(HttpMethod.Get, "/management/reports");
        okReq.Headers.Add("Cookie", mgrAuth);
        var okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        // Remove MANAGER role from DB
        db.UserRoles.Remove(userRole);
        await db.SaveChangesAsync();

        // Access portal -> Should be rejected
        var rejectedReq = new HttpRequestMessage(HttpMethod.Get, "/management/reports");
        rejectedReq.Headers.Add("Cookie", mgrAuth);
        var rejectedResp = await client.SendAsync(rejectedReq);

        Assert.True(rejectedResp.StatusCode == HttpStatusCode.Redirect ||
                    rejectedResp.StatusCode == HttpStatusCode.Forbidden ||
                    rejectedResp.StatusCode == HttpStatusCode.Unauthorized);

        if (rejectedResp.StatusCode == HttpStatusCode.Redirect)
        {
            var location = rejectedResp.Headers.Location?.ToString() ?? string.Empty;
            Assert.True(location.Contains("access-denied") || location.Contains("login"));
        }
    }

    [Fact]
    public async Task TC_MGMT_27_SalesDashboard_ExactKpiCalculation_AndVnTimeFiltering()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var queryService = new ManagementQueryService(db, new SystemClock());

        // Call dashboard without filters
        var report = await queryService.GetSalesDashboardAsync(null, null, null, null, 1, 15);

        // Query raw view directly from DB
        var viewSales = await db.ShowtimeSales.ToListAsync();

        var expectedRevenue = viewSales.Sum(s => s.RevenueVnd);
        var expectedTickets = viewSales.Sum(s => s.TicketsSold);
        var expectedCapacity = viewSales.Sum(s => s.SellableCapacity);
        var expectedWeightedOcc = expectedCapacity > 0
            ? Math.Round((decimal)expectedTickets * 100m / expectedCapacity, 2)
            : 0m;

        Assert.Equal(expectedRevenue, report.TotalRevenueVnd);
        Assert.Equal(expectedTickets, report.TotalTicketsSold);
        Assert.Equal(expectedWeightedOcc, report.WeightedOccupancyPct);

        // Verify non-paid orders are excluded from revenue
        var unpaidOrders = await db.Orders
            .Where(o => o.Status != OrderStates.Paid)
            .ToListAsync();

        // Any showtime with only unpaid orders must have 0 tickets and 0 revenue in the view
        foreach (var uo in unpaidOrders)
        {
            var hasPaidOnSameShowtime = await db.Orders.AnyAsync(o => o.ShowtimeId == uo.ShowtimeId && o.Status == OrderStates.Paid);
            if (!hasPaidOnSameShowtime)
            {
                var stSale = viewSales.FirstOrDefault(s => s.ShowtimeId == uo.ShowtimeId);
                if (stSale != null)
                {
                    Assert.Equal(0m, stSale.RevenueVnd);
                    Assert.Equal(0L, stSale.TicketsSold);
                }
            }
        }
    }

    [Fact]
    public async Task TC_MGMT_28_Dashboard_IndependentFinancialReconciliation_CalculatesCorrectly()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var managerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customerUser = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        // 1. Create two distinct movies
        var movie1 = new Movie
        {
            Title = "Independent Alpha " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 100,
            Status = "PUBLISHED"
        };
        var movie2 = new Movie
        {
            Title = "Independent Beta " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 110,
            Status = "PUBLISHED"
        };
        db.Movies.AddRange(movie1, movie2);
        await db.SaveChangesAsync();

        // 2. Reference two distinct rooms
        var room1 = await db.Rooms.FirstAsync();
        var room2 = new Room
        {
            RoomCode = "ROOM_TEST_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            RoomName = "Phong Chieu 2 Test",
            Status = "ACTIVE"
        };
        db.Rooms.Add(room2);
        await db.SaveChangesAsync();

        for (short col = 1; col <= 10; col++)
        {
            db.Seats.Add(new Seat
            {
                RoomId = room2.RoomId,
                SeatCode = $"B{col:D2}",
                RowLabel = "B",
                ColumnNo = col,
                SeatTypeCode = "VIP",
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // 3. Showtimes across UTC / VN boundary:
        // Day 1 VN: 2028-05-10 16:00 UTC = 2028-05-10 23:00 VN (UTC+7)
        // Day 2 VN: 2028-05-10 18:00 UTC = 2028-05-11 01:00 VN (UTC+7)
        var startUtc1 = new DateTime(2028, 5, 10, 16, 0, 0, DateTimeKind.Utc);
        var startUtc2 = new DateTime(2028, 5, 10, 18, 0, 0, DateTimeKind.Utc);

        var showtime1 = new Showtime
        {
            MovieId = movie1.MovieId,
            RoomId = room1.RoomId,
            StartUtc = startUtc1,
            EndUtc = startUtc1.AddMinutes(100),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        var showtime2 = new Showtime
        {
            MovieId = movie2.MovieId,
            RoomId = room2.RoomId,
            StartUtc = startUtc2,
            EndUtc = startUtc2.AddMinutes(110),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 20,
            CreatedBy = managerUser.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.AddRange(showtime1, showtime2);
        await db.SaveChangesAsync();

        var room1Seats = await db.Seats.Where(s => s.RoomId == room1.RoomId && s.IsActive).Take(4).ToListAsync();
        var room2Seats = await db.Seats.Where(s => s.RoomId == room2.RoomId && s.IsActive).Take(6).ToListAsync();

        // 4. Create synthetic orders across various lifecycle statuses:
        // Showtime 1 Orders:
        // - Order 1: PAID (2 seats @ 75,000 = 150,000 VND) with 2 Tickets issued
        var order1 = new BookingOrder
        {
            OrderCode = "BGP-FIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime1.ShowtimeId,
            Status = OrderStates.Paid,
            SeatCount = 2,
            SubtotalVnd = 150000,
            TotalVnd = 150000,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15),
            PaidUtc = DateTime.UtcNow
        };
        // - Order 2: CANCELLED (1 seat @ 75,000) - must NOT count
        var order2 = new BookingOrder
        {
            OrderCode = "BGP-FIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime1.ShowtimeId,
            Status = OrderStates.Cancelled,
            SeatCount = 1,
            SubtotalVnd = 75000,
            TotalVnd = 75000,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15)
        };
        // - Order 3: EXPIRED (1 seat @ 75,000) - must NOT count
        var order3 = new BookingOrder
        {
            OrderCode = "BGP-FIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime1.ShowtimeId,
            Status = OrderStates.Expired,
            SeatCount = 1,
            SubtotalVnd = 75000,
            TotalVnd = 75000,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-20),
            ExpiresUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        db.Orders.AddRange(order1, order2, order3);
        await db.SaveChangesAsync();

        // Add OrderSeats and ShowtimeSeats for Showtime 1
        db.OrderSeats.Add(new OrderSeat { OrderId = order1.OrderId, ShowtimeId = showtime1.ShowtimeId, SeatId = room1Seats[0].SeatId, SeatTypeSnapshot = "STANDARD", UnitPriceVnd = 75000 });
        db.OrderSeats.Add(new OrderSeat { OrderId = order1.OrderId, ShowtimeId = showtime1.ShowtimeId, SeatId = room1Seats[1].SeatId, SeatTypeSnapshot = "STANDARD", UnitPriceVnd = 75000 });
        db.OrderSeats.Add(new OrderSeat { OrderId = order2.OrderId, ShowtimeId = showtime1.ShowtimeId, SeatId = room1Seats[2].SeatId, SeatTypeSnapshot = "STANDARD", UnitPriceVnd = 75000 });
        db.OrderSeats.Add(new OrderSeat { OrderId = order3.OrderId, ShowtimeId = showtime1.ShowtimeId, SeatId = room1Seats[3].SeatId, SeatTypeSnapshot = "STANDARD", UnitPriceVnd = 75000 });

        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime1.ShowtimeId, RoomId = room1.RoomId, SeatId = room1Seats[0].SeatId, State = SeatStates.Sold, OwnerOrderId = order1.OrderId });
        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime1.ShowtimeId, RoomId = room1.RoomId, SeatId = room1Seats[1].SeatId, State = SeatStates.Sold, OwnerOrderId = order1.OrderId });
        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime1.ShowtimeId, RoomId = room1.RoomId, SeatId = room1Seats[2].SeatId, State = SeatStates.Available });
        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime1.ShowtimeId, RoomId = room1.RoomId, SeatId = room1Seats[3].SeatId, State = SeatStates.Available });

        var qr1 = new byte[32]; System.Security.Cryptography.RandomNumberGenerator.Fill(qr1);
        var qr2 = new byte[32]; System.Security.Cryptography.RandomNumberGenerator.Fill(qr2);
        db.Tickets.Add(new Ticket { TicketCode = "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), OrderId = order1.OrderId, ShowtimeId = showtime1.ShowtimeId, SeatId = room1Seats[0].SeatId, Status = "ISSUED", QrTokenHash = qr1, QrTokenCipher = new byte[48], QrKeyId = "k1", IssuedUtc = DateTime.UtcNow });
        db.Tickets.Add(new Ticket { TicketCode = "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), OrderId = order1.OrderId, ShowtimeId = showtime1.ShowtimeId, SeatId = room1Seats[1].SeatId, Status = "ISSUED", QrTokenHash = qr2, QrTokenCipher = new byte[48], QrKeyId = "k1", IssuedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Showtime 2 Orders:
        // - Order 4: PAID (3 seats @ 100,000 = 300,000 VND) with 3 Tickets issued + duplicate webhook simulation
        var order4 = new BookingOrder
        {
            OrderCode = "BGP-FIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime2.ShowtimeId,
            Status = OrderStates.Paid,
            SeatCount = 3,
            SubtotalVnd = 300000,
            TotalVnd = 300000,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15),
            PaidUtc = DateTime.UtcNow
        };
        // - Order 5: FAILED (2 seats @ 100,000) - must NOT count
        var order5 = new BookingOrder
        {
            OrderCode = "BGP-FIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime2.ShowtimeId,
            Status = OrderStates.Failed,
            SeatCount = 2,
            SubtotalVnd = 200000,
            TotalVnd = 200000,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15)
        };
        // - Order 6: NEEDS_REVIEW (1 seat @ 100,000) - must NOT count
        var order6 = new BookingOrder
        {
            OrderCode = "BGP-FIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            UserId = customerUser.UserId,
            ShowtimeId = showtime2.ShowtimeId,
            Status = OrderStates.Review,
            SeatCount = 1,
            SubtotalVnd = 100000,
            TotalVnd = 100000,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(15)
        };
        db.Orders.AddRange(order4, order5, order6);
        await db.SaveChangesAsync();

        db.OrderSeats.Add(new OrderSeat { OrderId = order4.OrderId, ShowtimeId = showtime2.ShowtimeId, SeatId = room2Seats[0].SeatId, SeatTypeSnapshot = "VIP", UnitPriceVnd = 100000 });
        db.OrderSeats.Add(new OrderSeat { OrderId = order4.OrderId, ShowtimeId = showtime2.ShowtimeId, SeatId = room2Seats[1].SeatId, SeatTypeSnapshot = "VIP", UnitPriceVnd = 100000 });
        db.OrderSeats.Add(new OrderSeat { OrderId = order4.OrderId, ShowtimeId = showtime2.ShowtimeId, SeatId = room2Seats[2].SeatId, SeatTypeSnapshot = "VIP", UnitPriceVnd = 100000 });

        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime2.ShowtimeId, RoomId = room2.RoomId, SeatId = room2Seats[0].SeatId, State = SeatStates.Sold, OwnerOrderId = order4.OrderId });
        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime2.ShowtimeId, RoomId = room2.RoomId, SeatId = room2Seats[1].SeatId, State = SeatStates.Sold, OwnerOrderId = order4.OrderId });
        db.ShowtimeSeats.Add(new ShowtimeSeat { ShowtimeId = showtime2.ShowtimeId, RoomId = room2.RoomId, SeatId = room2Seats[2].SeatId, State = SeatStates.Sold, OwnerOrderId = order4.OrderId });

        var qr3 = new byte[32]; System.Security.Cryptography.RandomNumberGenerator.Fill(qr3);
        var qr4 = new byte[32]; System.Security.Cryptography.RandomNumberGenerator.Fill(qr4);
        var qr5 = new byte[32]; System.Security.Cryptography.RandomNumberGenerator.Fill(qr5);
        db.Tickets.Add(new Ticket { TicketCode = "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), OrderId = order4.OrderId, ShowtimeId = showtime2.ShowtimeId, SeatId = room2Seats[0].SeatId, Status = "ISSUED", QrTokenHash = qr3, QrTokenCipher = new byte[48], QrKeyId = "k1", IssuedUtc = DateTime.UtcNow });
        db.Tickets.Add(new Ticket { TicketCode = "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), OrderId = order4.OrderId, ShowtimeId = showtime2.ShowtimeId, SeatId = room2Seats[1].SeatId, Status = "ISSUED", QrTokenHash = qr4, QrTokenCipher = new byte[48], QrKeyId = "k1", IssuedUtc = DateTime.UtcNow });
        db.Tickets.Add(new Ticket { TicketCode = "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), OrderId = order4.OrderId, ShowtimeId = showtime2.ShowtimeId, SeatId = room2Seats[2].SeatId, Status = "ISSUED", QrTokenHash = qr5, QrTokenCipher = new byte[48], QrKeyId = "k1", IssuedUtc = DateTime.UtcNow });

        // Add 2 SUCCESS payment events for Order 4 (simulating idempotent repeat webhook)
        db.Payments.Add(new Payment { OrderId = order4.OrderId, Provider = "MOCK", AttemptKey = "att-1", ProviderTransactionRef = "tx-1", AmountVnd = 300000, Status = "SUCCESS", CreatedUtc = DateTime.UtcNow, ResolvedUtc = DateTime.UtcNow });
        db.Payments.Add(new Payment { OrderId = order4.OrderId, Provider = "MOCK", AttemptKey = "att-2", ProviderTransactionRef = "tx-2", AmountVnd = 300000, Status = "SUCCESS", CreatedUtc = DateTime.UtcNow, ResolvedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // 5. Independent Mathematical Expectations:
        // Total Expected Revenue: 150,000 (Order 1) + 300,000 (Order 4) = 450,000 VND
        // Total Expected Tickets: 2 + 3 = 5 tickets
        // Total Expected Sellable Capacity: 10 (Room 1) + 20 (Room 2) = 30 seats
        // Weighted Occupancy Percentage: (5 * 100m) / 30m = 16.67%
        // Day 1 VN (2026-12-10): 150,000 VND
        // Day 2 VN (2026-12-11): 300,000 VND
        var queryService = new ManagementQueryService(db, new SystemClock());

        // A. Combined Date Range (2028-05-10 to 2028-05-11 in VN time)
        var fromUtcCombined = new DateTime(2028, 5, 10, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);
        var toUtcCombined = new DateTime(2028, 5, 11, 23, 59, 59, DateTimeKind.Utc).AddHours(-7);
        var combinedReport = await queryService.GetSalesDashboardAsync(fromUtcCombined, toUtcCombined, null, null);

        Assert.Equal(450000m, combinedReport.TotalRevenueVnd);
        Assert.Equal(5L, combinedReport.TotalTicketsSold);
        Assert.Equal(2, combinedReport.TotalShowtimes);
        Assert.Equal(16.67m, combinedReport.WeightedOccupancyPct);

        var mov1Rev = combinedReport.RevenueByMovie.First(m => m.MovieId == movie1.MovieId);
        Assert.Equal(150000m, mov1Rev.RevenueVnd);
        Assert.Equal(2L, mov1Rev.TicketsSold);

        var mov2Rev = combinedReport.RevenueByMovie.First(m => m.MovieId == movie2.MovieId);
        Assert.Equal(300000m, mov2Rev.RevenueVnd);
        Assert.Equal(3L, mov2Rev.TicketsSold);

        var rm1Rev = combinedReport.RevenueByRoom.First(r => r.RoomId == room1.RoomId);
        Assert.Equal(150000m, rm1Rev.RevenueVnd);
        Assert.Equal(2L, rm1Rev.TicketsSold);

        var rm2Rev = combinedReport.RevenueByRoom.First(r => r.RoomId == room2.RoomId);
        Assert.Equal(300000m, rm2Rev.RevenueVnd);
        Assert.Equal(3L, rm2Rev.TicketsSold);

        var day1Entry = combinedReport.RevenueByDay.First(d => d.DateVn == "2028-05-10");
        Assert.Equal(150000m, day1Entry.RevenueVnd);

        var day2Entry = combinedReport.RevenueByDay.First(d => d.DateVn == "2028-05-11");
        Assert.Equal(300000m, day2Entry.RevenueVnd);

        // B. Single Date Filter (Day 1 only: 2028-05-10 in VN time)
        var fromUtcDay1 = new DateTime(2028, 5, 10, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);
        var toUtcDay1 = new DateTime(2028, 5, 10, 23, 59, 59, DateTimeKind.Utc).AddHours(-7);
        var day1Report = await queryService.GetSalesDashboardAsync(fromUtcDay1, toUtcDay1, null, null);
        Assert.Equal(150000m, day1Report.TotalRevenueVnd);
        Assert.Equal(2L, day1Report.TotalTicketsSold);
        Assert.Equal(1, day1Report.TotalShowtimes);
        Assert.Equal(20.00m, day1Report.WeightedOccupancyPct);

        // C. Movie Filter (Movie 2 only)
        var mov2Report = await queryService.GetSalesDashboardAsync(fromUtcCombined, toUtcCombined, movie2.MovieId, null);
        Assert.Equal(300000m, mov2Report.TotalRevenueVnd);
        Assert.Equal(3L, mov2Report.TotalTicketsSold);
        Assert.Equal(1, mov2Report.TotalShowtimes);
        Assert.Equal(15.00m, mov2Report.WeightedOccupancyPct);

        // D. Room Filter (Room 1 only)
        var room1Report = await queryService.GetSalesDashboardAsync(fromUtcCombined, toUtcCombined, null, room1.RoomId);
        Assert.Equal(150000m, room1Report.TotalRevenueVnd);
        Assert.Equal(2L, room1Report.TotalTicketsSold);
        Assert.Equal(1, room1Report.TotalShowtimes);
        Assert.Equal(20.00m, room1Report.WeightedOccupancyPct);
    }

    [Fact]
    public async Task TC_MGMT_29_MovieArchive_ShowtimePublish_Concurrency_PreservesInvariants()
    {
        await using var dbSetup = await fixture.CreateDbContextAsync();
        var managerUser = await dbSetup.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var room = await dbSetup.Rooms.FirstAsync(r => r.RoomId == 1 && r.Status == "ACTIVE");

        // 1. Create a published movie with no showtimes
        var movie = new Movie
        {
            Title = "Concurrency Movie " + Guid.NewGuid().ToString("N")[..6],
            DurationMin = 90,
            Status = "PUBLISHED"
        };
        dbSetup.Movies.Add(movie);
        await dbSetup.SaveChangesAsync();

        var seatTypes = await dbSetup.SeatTypes.ToListAsync();
        var prices = seatTypes.Select(st => new SeatPriceInput(st.SeatTypeCode, 80000)).ToList();

        // 2. Concurrently execute Archive and Publish on two independent DbContext instances
        await using var dbArchive = await fixture.CreateDbContextAsync();
        await using var dbPublish = await fixture.CreateDbContextAsync();

        var movieService = new MovieManagementService(dbArchive, new SystemClock());
        var publishingService = new ShowtimePublishingService(dbPublish, new SystemClock());

        var futureShowtimeUtc = DateTime.UtcNow.AddDays(7);

        Exception? archiveException = null;
        Exception? publishException = null;

        var taskArchive = Task.Run(async () =>
        {
            try
            {
                await movieService.ArchiveMovieAsync(managerUser.UserId, movie.MovieId);
            }
            catch (Exception ex)
            {
                archiveException = ex;
            }
        });

        var taskPublish = Task.Run(async () =>
        {
            try
            {
                await publishingService.PublishAsync(
                    managerUser.UserId, room.RoomId, movie.MovieId, futureShowtimeUtc, 15, prices);
            }
            catch (Exception ex)
            {
                publishException = ex;
            }
        });

        await Task.WhenAll(taskArchive, taskPublish);

        // 3. Verify Database Invariant via fresh DbContext:
        await using var dbVerify = await fixture.CreateDbContextAsync();
        var reloadedMovie = await dbVerify.Movies.SingleAsync(m => m.MovieId == movie.MovieId);
        var activeShowtimes = await dbVerify.Showtimes
            .Where(s => s.MovieId == movie.MovieId && s.Status != "CANCELLED" && s.EndUtc > DateTime.UtcNow)
            .ToListAsync();

        // Strict invariant: Under NO circumstance does an ARCHIVED movie have an ON_SALE future showtime
        if (reloadedMovie.Status == "ARCHIVED")
        {
            Assert.Empty(activeShowtimes);
            Assert.Null(archiveException);
            Assert.NotNull(publishException);
            var bEx = Assert.IsAssignableFrom<BusinessRuleException>(publishException);
            Assert.Equal("NOT_PUBLISHABLE", bEx.Code);
        }
        else
        {
            Assert.Equal("PUBLISHED", reloadedMovie.Status);
            Assert.NotEmpty(activeShowtimes);
            Assert.Null(publishException);
            Assert.NotNull(archiveException);
            var bEx = Assert.IsAssignableFrom<BusinessRuleException>(archiveException);
            Assert.Equal("CANNOT_ARCHIVE_ACTIVE_SHOWTIMES", bEx.Code);
        }
    }

    [Fact]
    public async Task TC_MGMT_25_ExternalLookupApi_EnforcesManagerRbac_AndHandlesMissingTmdbTokenGracefully()
    {
        var client = CreateNewClient();

        // 1. Anonymous -> 401 or redirect to login
        var anonResp = await client.GetAsync("/api/management/movies/external-lookup?query=tt0807840");
        Assert.True(
            anonResp.StatusCode == HttpStatusCode.Unauthorized || anonResp.StatusCode == HttpStatusCode.Redirect,
            $"Expected 401 or Redirect for anonymous, got {anonResp.StatusCode}");

        // 2. CUSTOMER -> 403 or redirect to access-denied
        var (custAuth, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");
        var custReq = new HttpRequestMessage(HttpMethod.Get, "/api/management/movies/external-lookup?query=tt0807840");
        custReq.Headers.Add("Cookie", custAuth);
        var custResp = await client.SendAsync(custReq);
        Assert.True(
            custResp.StatusCode == HttpStatusCode.Forbidden ||
            (custResp.StatusCode == HttpStatusCode.Redirect && custResp.Headers.Location?.ToString().Contains("access-denied") == true),
            $"Expected 403 or redirect to access-denied for CUSTOMER, got {custResp.StatusCode}");

        // 3. STAFF -> 403 or redirect to access-denied
        var (staffAuth, _, _) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");
        var staffReq = new HttpRequestMessage(HttpMethod.Get, "/api/management/movies/external-lookup?query=tt0807840");
        staffReq.Headers.Add("Cookie", staffAuth);
        var staffResp = await client.SendAsync(staffReq);
        Assert.True(
            staffResp.StatusCode == HttpStatusCode.Forbidden ||
            (staffResp.StatusCode == HttpStatusCode.Redirect && staffResp.Headers.Location?.ToString().Contains("access-denied") == true),
            $"Expected 403 or redirect to access-denied for STAFF, got {staffResp.StatusCode}");

        // 4. MANAGER -> 200 OK on verified entry (Elephants Dream tt0807840)
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");
        var mgrReq = new HttpRequestMessage(HttpMethod.Get, "/api/management/movies/external-lookup?query=tt0807840");
        mgrReq.Headers.Add("Cookie", mgrAuth);
        var mgrResp = await client.SendAsync(mgrReq);
        Assert.Equal(HttpStatusCode.OK, mgrResp.StatusCode);

        var lookupJson = await mgrResp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(lookupJson);
        Assert.Equal("OK", doc.RootElement.GetProperty("statusCode").GetString());
        var candidates = doc.RootElement.GetProperty("items");
        Assert.True(candidates.GetArrayLength() >= 1);
        var first = candidates[0];
        Assert.Equal("Elephants Dream", first.GetProperty("title").GetString());
        Assert.Equal("Q737719", first.GetProperty("wikidataQid").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("ageLabelVn").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("ratingValue").ValueKind);

        // 5. Unknown numeric TMDB ID when BGP_TMDB_READ_TOKEN is absent -> graceful 200 OK (never 500)
        var missingReq = new HttpRequestMessage(HttpMethod.Get, "/api/management/movies/external-lookup?query=999888777");
        missingReq.Headers.Add("Cookie", mgrAuth);
        var missingResp = await client.SendAsync(missingReq);
        Assert.Equal(HttpStatusCode.OK, missingResp.StatusCode);

        var missingJson = await missingResp.Content.ReadAsStringAsync();
        using var missingDoc = System.Text.Json.JsonDocument.Parse(missingJson);
        var missingStatus = missingDoc.RootElement.GetProperty("statusCode").GetString();
        Assert.Contains(missingStatus, new[] { "TMDB_TOKEN_MISSING", "NO_MATCH" });
    }

    [Fact]
    public async Task TC_MGMT_26_SyncExternalMovie_ImportsDraft_AndPreservesManualOverrideLocksOnResync()
    {
        var client = CreateNewClient();
        var (mgrAuth, _, _) = await LoginAndGetTokensAsync(client, "manager@demo.invalid");

        // Verify Zero-Bootstrap on /management/movies
        var (listAfCookie, listAfToken) = await ExtractAntiforgeryTokenAsync(client, "/management/movies", mgrAuth);
        var listCheckReq = new HttpRequestMessage(HttpMethod.Get, "/management/movies");
        listCheckReq.Headers.Add("Cookie", mgrAuth);
        var listCheckResp = await client.SendAsync(listCheckReq);
        var listHtml = await listCheckResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bootstrap.min.css", listHtml);
        Assert.DoesNotContain("dark-compat.css", listHtml);

        // 1. Import Big Buck Bunny (2008) from verified external registry -> DRAFT with null AgeLabel
        var syncReq = new HttpRequestMessage(HttpMethod.Post, "/management/movies/sync-external")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = listAfToken,
                ["VerifiedKey"] = "big-buck-bunny-2008"
            })
        };
        syncReq.Headers.Add("Cookie", $"{mgrAuth}; {listAfCookie}");
        var syncResp = await client.SendAsync(syncReq);
        Assert.Equal(HttpStatusCode.Redirect, syncResp.StatusCode);

        var location = syncResp.Headers.Location?.ToString() ?? string.Empty;
        var idMatch = Regex.Match(location, @"/management/movies/(\d+)$");
        Assert.True(idMatch.Success, $"Expected redirect to /management/movies/{{id}}, got {location}");
        var movieId = long.Parse(idMatch.Groups[1].Value);

        await using (var dbCheck = await fixture.CreateDbContextAsync())
        {
            var imported = await dbCheck.Movies.SingleAsync(m => m.MovieId == movieId);
            Assert.Equal("DRAFT", imported.Status);
            Assert.Equal("Q731114", imported.WikidataQid);
            Assert.Equal("tt1254207", imported.ImdbId);
            Assert.Null(imported.AgeLabel);
            Assert.Null(imported.RatingValue);
        }

        // 2. Edit movie with Vietnamese release title + VN AgeLabel "P" and explicit ManualOverrideMask locks
        var (editAfCookie, editAfToken) = await ExtractAntiforgeryTokenAsync(client, $"/management/movies/{movieId}/edit", mgrAuth);
        var customTitle = "Chú Thỏ Khổng Lồ (Bản Chiếu Rạp VN)";
        var editPairs = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", editAfToken),
            new("MovieId", movieId.ToString()),
            new("Title", customTitle),
            new("OriginalTitle", "Big Buck Bunny"),
            new("ReleaseYear", "2008"),
            new("DurationMin", "10"),
            new("GenreText", "Hoạt hình, Hài hước, Gia đình"),
            new("AgeLabel", "P"),
            new("DirectorNames", "Sacha Goedegebure"),
            new("OriginCountry", "Hà Lan"),
            new("OriginalLanguage", "Không lời"),
            new("WikidataQid", "Q731114"),
            new("ImdbId", "tt1254207"),
            new("TmdbId", "10378"),
            new("PosterUrl", "/media/verified/big-buck-bunny-2008-poster.jpg"),
            new("BackdropUrl", "/media/verified/big-buck-bunny-2008-backdrop.jpg"),
            new("ApplyOverrideLocks", "true"),
            new("ManualOverrides", "title"),
            new("ManualOverrides", "age_label")
        };

        var editReq = new HttpRequestMessage(HttpMethod.Post, $"/management/movies/{movieId}/edit")
        {
            Content = new FormUrlEncodedContent(editPairs)
        };
        editReq.Headers.Add("Cookie", $"{mgrAuth}; {editAfCookie}");
        var editResp = await client.SendAsync(editReq);
        Assert.Equal(HttpStatusCode.Redirect, editResp.StatusCode);

        // 3. Re-sync external metadata -> locked fields (title, age_label) MUST remain intact!
        var (detailAfCookie, detailAfToken) = await ExtractAntiforgeryTokenAsync(client, $"/management/movies/{movieId}", mgrAuth);
        var resyncReq = new HttpRequestMessage(HttpMethod.Post, "/management/movies/sync-external")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = detailAfToken,
                ["VerifiedKey"] = "big-buck-bunny-2008",
                ["TargetMovieId"] = movieId.ToString()
            })
        };
        resyncReq.Headers.Add("Cookie", $"{mgrAuth}; {detailAfCookie}");
        var resyncResp = await client.SendAsync(resyncReq);
        Assert.Equal(HttpStatusCode.Redirect, resyncResp.StatusCode);

        await using (var dbVerify = await fixture.CreateDbContextAsync())
        {
            var resynced = await dbVerify.Movies.SingleAsync(m => m.MovieId == movieId);
            Assert.Equal(customTitle, resynced.Title);
            Assert.Equal("P", resynced.AgeLabel);
            Assert.True(resynced.HasManualOverride("title"));
            Assert.True(resynced.HasManualOverride("age_label"));
            Assert.Equal("Q731114", resynced.WikidataQid);
        }
    }
}
