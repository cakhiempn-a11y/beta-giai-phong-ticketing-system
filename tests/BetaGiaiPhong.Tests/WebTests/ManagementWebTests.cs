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
}
