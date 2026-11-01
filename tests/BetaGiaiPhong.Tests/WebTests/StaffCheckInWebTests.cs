using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Tests.Helpers;
using BetaGiaiPhong.Tests.IntegrationTests;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.Responses;
using Xunit;

namespace BetaGiaiPhong.Tests.WebTests;

[Collection("DatabaseCollection")]
public sealed class StaffCheckInWebTests(TestDatabaseFixture fixture) : IClassFixture<CustomWebApplicationFactory>
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

    private static async Task<(string Cookie, string Token)> ExtractAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
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
        var xsrfCookieHeader = getResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains("XSRF-TOKEN"));

        var finalAfCookie = newAfCookieHeader != null ? newAfCookieHeader.Split(';')[0] : afCookie;
        var xsrfToken = xsrfCookieHeader != null
            ? Uri.UnescapeDataString(xsrfCookieHeader.Split(';')[0].Replace("XSRF-TOKEN=", ""))
            : afToken;

        return (authCookie, finalAfCookie, xsrfToken);
    }

    private async Task<(long ShowtimeId, long TicketId, string RawToken, long SeatId)> CreatePaidTicketAsync(
        DateTime? customStartUtc = null)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.FirstAsync();
        var room = await db.Rooms.FirstAsync();
        var manager = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");
        var customer = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");

        // By default, showtime is active right now (within valid 30m scan window)
        var startUtc = customStartUtc ?? DateTime.UtcNow.AddMinutes(10);
        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(120),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 10,
            CreatedBy = manager.UserId,
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
        foreach (var st in await db.SeatTypes.ToListAsync())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st.SeatTypeCode,
                AmountVnd = 75000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var res = await new SeatReservationService(db, new SystemClock())
            .ReserveAsync(customer.UserId, showtime.ShowtimeId, [seat.SeatId]);

        var paymentService = new PaymentService(db, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customer.UserId, res.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-STAFF-" + Guid.NewGuid().ToString("N"), res.OrderCode, res.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        await paymentService.ApplyVerifiedMockAsync(verified);

        var ticket = await db.Tickets.SingleAsync(t => t.OrderId == res.OrderId);
        var token = new QrTokenProtector().Unseal(ticket.QrTokenCipher);

        return (showtime.ShowtimeId, ticket.TicketId, token, seat.SeatId);
    }

    [Fact]
    public async Task TC_WEB_STAFF_01_Guest_CannotAccessStaffCheckIn_RedirectsToLogin()
    {
        var client = CreateNewClient();
        var response = await client.GetAsync("/staff/check-in");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/account/login", response.Headers.Location.ToString());
    }

    [Fact]
    public async Task TC_WEB_STAFF_02_Customer_CannotAccessStaffCheckIn_Returns403Forbidden()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var request = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in");
        request.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(request);
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString().Contains("access-denied") == true),
            $"Expected 403 or redirect to access-denied, got: {response.StatusCode}");
    }

    [Fact]
    public async Task TC_WEB_STAFF_03_Staff_CanAccessStaffCheckIn_Returns200OK()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var request = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in");
        request.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Màn hình Soát vé Điện tử", html);
        Assert.Contains("html5-qrcode", html);
    }

    [Fact]
    public async Task TC_WEB_STAFF_04_Staff_CanAccessCheckInHistory_Returns200OK()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var request = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in/history");
        request.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Lịch Sử Soát Vé", html);
    }

    [Fact]
    public async Task TC_WEB_STAFF_05_ScanTicketApi_Unauthenticated_Returns401Unauthorized()
    {
        var client = CreateNewClient();
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");

        var payload = new ScanTicketRequest { QrToken = "dummy_token", ExpectedShowtimeId = 1 };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", afCookie);
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_STAFF_06_ScanTicketApi_CustomerRole_Returns403Forbidden()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var payload = new ScanTicketRequest { QrToken = "dummy_token", ExpectedShowtimeId = 1 };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_STAFF_07_ScanTicketApi_MissingAntiforgery_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var payload = new ScanTicketRequest { QrToken = "dummy_token", ExpectedShowtimeId = 1 };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", authCookie);
        // Missing X-XSRF-TOKEN header

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_STAFF_08_ScanTicketApi_InvalidPayload_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        // Empty token and negative showtime ID
        var payload = new ScanTicketRequest { QrToken = "", ExpectedShowtimeId = -5 };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_STAFF_09_ScanTicketApi_ValidTicket_Returns200Accepted_TransitionsToUsed()
    {
        var (showtimeId, ticketId, rawToken, _) = await CreatePaidTicketAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var payload = new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(result);
        Assert.Equal("ACCEPTED", result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Equal(ticketId, result.TicketId);
        Assert.False(string.IsNullOrWhiteSpace(result.TicketCode));
        Assert.NotNull(result.MovieTitle);
        Assert.NotNull(result.SeatLabel);

        // Verify ticket status in DB transitioned to USED
        await using var db = await fixture.CreateDbContextAsync();
        var ticket = await db.Tickets.SingleAsync(t => t.TicketId == ticketId);
        Assert.Equal("USED", ticket.Status);
        Assert.NotNull(ticket.UsedUtc);

        // Verify TicketScan audit record was written
        var scan = await db.TicketScans.SingleOrDefaultAsync(s => s.TicketId == ticketId && s.Outcome == "ACCEPTED");
        Assert.NotNull(scan);
    }

    [Fact]
    public async Task TC_WEB_STAFF_10_ScanTicketApi_RescanSameTicket_Returns409AlreadyUsed()
    {
        var (showtimeId, ticketId, rawToken, _) = await CreatePaidTicketAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        // Scan 1: Accept
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId })
        };
        req1.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req1.Headers.Add("X-XSRF-TOKEN", afToken);
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // Scan 2: Already Used -> 409 Conflict
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId })
        };
        req2.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req2.Headers.Add("X-XSRF-TOKEN", afToken);
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);

        var result2 = await resp2.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(result2);
        Assert.Equal("ALREADY_USED", result2.Outcome);
        Assert.False(result2.IsSuccess);
        Assert.Equal(ticketId, result2.TicketId);
    }

    [Fact]
    public async Task TC_WEB_STAFF_11_ScanTicketApi_WrongShowtime_Returns422WrongShowtime()
    {
        var (showtimeId, ticketId, rawToken, _) = await CreatePaidTicketAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        // Supply a different expectedShowtimeId (showtimeId + 9999)
        var payload = new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId + 9999 };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(result);
        Assert.Equal("WRONG_SHOWTIME", result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal(ticketId, result.TicketId);
    }

    [Fact]
    public async Task TC_WEB_STAFF_12_ScanTicketApi_OutsideWindow_Returns422OutsideWindow()
    {
        // Create ticket for showtime starting 5 hours in the future (outside -30m window)
        var futureTime = DateTime.UtcNow.AddHours(5);
        var (showtimeId, ticketId, rawToken, _) = await CreatePaidTicketAsync(futureTime);

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var payload = new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(result);
        Assert.Equal("OUTSIDE_WINDOW", result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal(ticketId, result.TicketId);
    }

    [Fact]
    public async Task TC_WEB_STAFF_13_ScanTicketApi_FakeToken_Returns404NotFound_WritesAuditLog()
    {
        var (showtimeId, _, _, _) = await CreatePaidTicketAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var fakeToken = "BGP-FORGED-TOKEN-" + Guid.NewGuid().ToString("N");
        var payload = new ScanTicketRequest { QrToken = fakeToken, ExpectedShowtimeId = showtimeId };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(result);
        Assert.Equal("NOT_FOUND", result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.TicketId);

        // Verify AuditLog written
        await using var db = await fixture.CreateDbContextAsync();
        var audit = await db.AuditLogs.AnyAsync(a => a.EventType == "TICKET_SCAN_REJECTED");
        Assert.True(audit);
    }

    [Fact]
    public async Task TC_WEB_STAFF_14_ScanTicketApi_ConcurrentScan_ExactlyOneAccepted_OneAlreadyUsed()
    {
        var (showtimeId, ticketId, rawToken, _) = await CreatePaidTicketAsync();

        var client1 = CreateNewClient();
        var (auth1, af1, tok1) = await LoginAndGetTokensAsync(client1, "staff@demo.invalid");

        var client2 = CreateNewClient();
        var (auth2, af2, tok2) = await LoginAndGetTokensAsync(client2, "manager@demo.invalid");

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId })
        };
        req1.Headers.Add("Cookie", $"{auth1}; {af1}");
        req1.Headers.Add("X-XSRF-TOKEN", tok1);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId })
        };
        req2.Headers.Add("Cookie", $"{auth2}; {af2}");
        req2.Headers.Add("X-XSRF-TOKEN", tok2);

        var task1 = client1.SendAsync(req1);
        var task2 = client2.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        var outcomes = new List<string>();
        foreach (var resp in responses)
        {
            var dto = await resp.Content.ReadFromJsonAsync<ScanTicketResponse>();
            Assert.NotNull(dto);
            outcomes.Add(dto.Outcome);
        }

        Assert.Contains("ACCEPTED", outcomes);
        Assert.Contains("ALREADY_USED", outcomes);
    }

    [Fact]
    public async Task TC_WEB_STAFF_15_ScanTicketApi_DoesNotLeakRawTokenOrSecrets()
    {
        var (showtimeId, _, rawToken, _) = await CreatePaidTicketAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var payload = new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId };
        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(payload)
        };
        postReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        postReq.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(postReq);
        var jsonText = await response.Content.ReadAsStringAsync();

        // Ensure raw token, ciphers, hash, or secret keys are never returned in JSON
        Assert.DoesNotContain(rawToken, jsonText);
        Assert.DoesNotContain("qrTokenCipher", jsonText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qrTokenHash", jsonText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BGP_QR_AES_KEY", jsonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TC_WEB_STAFF_16_EndToEnd_FromOrderPaymentToQrDecodeAndStaffScan()
    {
        // 1. Create paid ticket
        var (showtimeId, ticketId, expectedRawToken, _) = await CreatePaidTicketAsync();

        // 2. Customer logs in and downloads QR PNG image
        var customerClient = CreateNewClient();
        var (custAuth, _, _) = await LoginAndGetTokensAsync(customerClient, "customer1@demo.invalid");

        var qrReq = new HttpRequestMessage(HttpMethod.Get, $"/tickets/{ticketId}/qr");
        qrReq.Headers.Add("Cookie", custAuth);
        var qrResp = await customerClient.SendAsync(qrReq);
        Assert.Equal(HttpStatusCode.OK, qrResp.StatusCode);
        var pngBytes = await qrResp.Content.ReadAsByteArrayAsync();

        // 3. Decode token from raw PNG image bytes using pure C# decoder
        var decodedToken = QrPngDecoder.Decode(pngBytes);
        Assert.Equal(expectedRawToken, decodedToken);

        // 4. Staff logs in and scans the decoded token via Staff API
        var staffClient = CreateNewClient();
        var (staffAuth, staffAf, staffTok) = await LoginAndGetTokensAsync(staffClient, "staff@demo.invalid");

        var scanReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = decodedToken, ExpectedShowtimeId = showtimeId })
        };
        scanReq.Headers.Add("Cookie", $"{staffAuth}; {staffAf}");
        scanReq.Headers.Add("X-XSRF-TOKEN", staffTok);

        var scanResp = await staffClient.SendAsync(scanReq);
        Assert.Equal(HttpStatusCode.OK, scanResp.StatusCode);

        var scanDto = await scanResp.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(scanDto);
        Assert.Equal("ACCEPTED", scanDto.Outcome);
        Assert.True(scanDto.IsSuccess);
        Assert.Equal(ticketId, scanDto.TicketId);

        // 5. Subsequent scan must be ALREADY_USED
        var rescanReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = decodedToken, ExpectedShowtimeId = showtimeId })
        };
        rescanReq.Headers.Add("Cookie", $"{staffAuth}; {staffAf}");
        rescanReq.Headers.Add("X-XSRF-TOKEN", staffTok);

        var rescanResp = await staffClient.SendAsync(rescanReq);
        Assert.Equal(HttpStatusCode.Conflict, rescanResp.StatusCode);
        var rescanDto = await rescanResp.Content.ReadFromJsonAsync<ScanTicketResponse>();
        Assert.NotNull(rescanDto);
        Assert.Equal("ALREADY_USED", rescanDto.Outcome);
    }

    [Fact]
    public async Task TC_WEB_STAFF_17_CheckInHistory_DisplaysRecordedScansAndAudits()
    {
        var (showtimeId, ticketId, rawToken, _) = await CreatePaidTicketAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        // Scan valid ticket
        var scanReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = rawToken, ExpectedShowtimeId = showtimeId })
        };
        scanReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        scanReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var scanResp = await client.SendAsync(scanReq);
        Assert.Equal(HttpStatusCode.OK, scanResp.StatusCode);

        // Scan fake token (NOT_FOUND)
        var fakeReq = new HttpRequestMessage(HttpMethod.Post, "/api/staff/tickets/scan")
        {
            Content = JsonContent.Create(new ScanTicketRequest { QrToken = "INVALID_TOKEN_FOR_HISTORY", ExpectedShowtimeId = showtimeId })
        };
        fakeReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        fakeReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var fakeResp = await client.SendAsync(fakeReq);
        Assert.Equal(HttpStatusCode.NotFound, fakeResp.StatusCode);

        // 1. View History with showtimeId filter -> verifies ACCEPTED is displayed in table
        var showtimeHistReq = new HttpRequestMessage(HttpMethod.Get, $"/staff/check-in/history?showtimeId={showtimeId}");
        showtimeHistReq.Headers.Add("Cookie", authCookie);
        var showtimeHistResp = await client.SendAsync(showtimeHistReq);
        Assert.Equal(HttpStatusCode.OK, showtimeHistResp.StatusCode);
        var showtimeHtml = await showtimeHistResp.Content.ReadAsStringAsync();
        Assert.Contains("ACCEPTED", showtimeHtml);

        // 2. View History with outcome=NOT_FOUND filter -> verifies NOT_FOUND audit logs are displayed in table
        var notFoundHistReq = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in/history?outcome=NOT_FOUND");
        notFoundHistReq.Headers.Add("Cookie", authCookie);
        var notFoundHistResp = await client.SendAsync(notFoundHistReq);
        Assert.Equal(HttpStatusCode.OK, notFoundHistResp.StatusCode);
        var notFoundHtml = await notFoundHistResp.Content.ReadAsStringAsync();
        Assert.Contains("NOT_FOUND", notFoundHtml);
    }

    [Fact]
    public async Task TC_WEB_STAFF_18_ClientScannerView_DoesNotLogRawTokens_AndUsesSafeRendering()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var getReq = new HttpRequestMessage(HttpMethod.Get, "/staff/check-in");
        getReq.Headers.Add("Cookie", authCookie);
        var response = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // 1. Security: Must NOT contain raw token logging
        Assert.DoesNotContain("console.log(\"Debounced duplicate scan:", html);
        Assert.DoesNotContain("console.log(\"Raw QR Token", html);

        // 2. Safe DOM rendering: Must use textContent and createElement rather than insecure innerHTML interpolation
        Assert.Contains("textContent", html);
        Assert.Contains("createElement", html);

        // 3. Duplicate scan prevention & reset flow + isolated deterministic QR file decoder
        Assert.Contains("btnScanNext", html);
        Assert.Contains("btnRescanCurrent", html);
        Assert.Contains("isInFlight", html);
        Assert.Contains("isTicketAccepted", html);
        Assert.Contains("qrFileScratch", html);
        Assert.Contains("decodeQrFileDeterministically", html);
        Assert.Contains("INVALID_QR_IMAGE", html);
    }
}
