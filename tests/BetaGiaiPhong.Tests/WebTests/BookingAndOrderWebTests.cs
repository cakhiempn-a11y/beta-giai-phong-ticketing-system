using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Tests.IntegrationTests;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.Responses;
using Xunit;

namespace BetaGiaiPhong.Tests.WebTests;

[Collection("DatabaseCollection")]
public sealed class BookingAndOrderWebTests(TestDatabaseFixture fixture) : IClassFixture<CustomWebApplicationFactory>
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

    private async Task<long> CreateIsolatedShowtimeAsync(int seatCount = 10)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.FirstAsync();
        var room = await db.Rooms.FirstAsync();
        var manager = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");

        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = DateTime.UtcNow.AddDays(2),
            EndUtc = DateTime.UtcNow.AddDays(2).AddMinutes(120),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = seatCount,
            CreatedBy = manager.UserId,
            CreatedUtc = DateTime.UtcNow
        };
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();

        var seats = await db.Seats.Where(s => s.RoomId == room.RoomId && s.IsActive).Take(seatCount).ToListAsync();
        foreach (var seat in seats)
        {
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = seat.SeatId,
                RoomId = room.RoomId,
                State = SeatStates.Available
            });
        }
        foreach (var st in await db.SeatTypes.ToListAsync())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatTypeCode = st.SeatTypeCode,
                AmountVnd = 70000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return showtime.ShowtimeId;
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

    private async Task<(string AuthCookie, string AfCookie, string AfToken)> LoginAndGetTokensAsync(
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
            .First(c => c.Contains(".BetaGiaiPhong.Auth"))
            .Split(';')[0];

        // Fetch / to refresh anti-forgery token tied to the authenticated user identity
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

    [Fact]
    public async Task TC_WEB_BOOK_01_Guest_PostReserve_Returns401Unauthorized()
    {
        var client = CreateNewClient();
        var request = new ReserveSeatsRequest { ShowtimeId = 1, SeatIds = [1] };

        var response = await client.PostAsJsonAsync("/api/bookings/reserve", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_02_NonCustomerRole_PostReserve_Returns403Forbidden()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "staff@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = 1, SeatIds = [1] })
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_03_Customer_ReserveSingleSeat_Returns201Created()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var result = await response.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(result);
        Assert.True(result.OrderId > 0);
        Assert.StartsWith("BGP-", result.OrderCode);
        Assert.Equal(70000, result.TotalVnd);
        Assert.Equal("PENDING_PAYMENT", result.Status);
        Assert.Contains(seat.SeatId, result.SeatIds);
    }

    [Fact]
    public async Task TC_WEB_BOOK_04_Customer_ReserveMultipleSeats_Returns201Created()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        await using var db = await fixture.CreateDbContextAsync();
        var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).Take(3).Select(s => s.SeatId).ToListAsync();

        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = seats })
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(3, result.SeatIds.Count);
        Assert.Equal(210000, result.TotalVnd);
    }

    [Fact]
    public async Task TC_WEB_BOOK_05_ReserveMoreThan8Seats_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var nineSeats = Enumerable.Range(1, 9).Select(i => (long)i).ToList();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = 1, SeatIds = nineSeats })
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_06_ReserveDuplicateSeats_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = 1, SeatIds = [5, 5] })
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_07_ReserveEmptySeatList_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = 1, SeatIds = [] })
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_08_ReserveAlreadyHeldSeat_Returns409Conflict()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        // Customer 1 reserves the seat
        var client1 = CreateNewClient();
        var (auth1, af1Cookie, af1Token) = await LoginAndGetTokensAsync(client1, "customer1@demo.invalid");
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        req1.Headers.Add("Cookie", $"{auth1}; {af1Cookie}");
        req1.Headers.Add("X-XSRF-TOKEN", af1Token);
        var resp1 = await client1.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        // Customer 2 tries to reserve the same held seat
        var client2 = CreateNewClient();
        var (auth2, af2Cookie, af2Token) = await LoginAndGetTokensAsync(client2, "customer2@demo.invalid");
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        req2.Headers.Add("Cookie", $"{auth2}; {af2Cookie}");
        req2.Headers.Add("X-XSRF-TOKEN", af2Token);
        var resp2 = await client2.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_09_ReserveSeatFromAnotherRoom_ReturnsConflict()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [999999] })
        };
        req.Headers.Add("Cookie", $"{auth}; {afCookie}");
        req.Headers.Add("X-XSRF-TOKEN", afToken);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_10_CustomerA_CannotViewCustomerB_Order_Returns404NotFound()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        // Customer 1 reserves
        var client1 = CreateNewClient();
        var (auth1, af1Cookie, af1Token) = await LoginAndGetTokensAsync(client1, "customer1@demo.invalid");
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        req1.Headers.Add("Cookie", $"{auth1}; {af1Cookie}");
        req1.Headers.Add("X-XSRF-TOKEN", af1Token);
        var resp1 = await client1.SendAsync(req1);
        var order1 = await resp1.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order1);

        // Customer 2 attempts to GET Customer 1's order
        var client2 = CreateNewClient();
        var (auth2, _, _) = await LoginAndGetTokensAsync(client2, "customer2@demo.invalid");
        var req2 = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order1.OrderId}");
        req2.Headers.Add("Cookie", auth2);

        var resp2 = await client2.SendAsync(req2);
        Assert.Equal(HttpStatusCode.NotFound, resp2.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_11_CustomerA_CannotCancelCustomerB_Order_Returns403Or404()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        // Customer 1 reserves
        var client1 = CreateNewClient();
        var (auth1, af1Cookie, af1Token) = await LoginAndGetTokensAsync(client1, "customer1@demo.invalid");
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        req1.Headers.Add("Cookie", $"{auth1}; {af1Cookie}");
        req1.Headers.Add("X-XSRF-TOKEN", af1Token);
        var resp1 = await client1.SendAsync(req1);
        var order1 = await resp1.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order1);

        // Customer 2 attempts to CANCEL Customer 1's order -> Must return 404 NotFound (does not disclose order existence)
        var client2 = CreateNewClient();
        var (auth2, af2Cookie, af2Token) = await LoginAndGetTokensAsync(client2, "customer2@demo.invalid");
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order1.OrderId}/cancel");
        req2.Headers.Add("Cookie", $"{auth2}; {af2Cookie}");
        req2.Headers.Add("X-XSRF-TOKEN", af2Token);

        var resp2 = await client2.SendAsync(req2);
        Assert.Equal(HttpStatusCode.NotFound, resp2.StatusCode);

        // Customer 1 attempts to CANCEL non-existent order -> Must also return 404 NotFound
        var nonExistentReq = new HttpRequestMessage(HttpMethod.Post, "/api/orders/99999999/cancel");
        nonExistentReq.Headers.Add("Cookie", $"{auth1}; {af1Cookie}");
        nonExistentReq.Headers.Add("X-XSRF-TOKEN", af1Token);
        var nonExistentResp = await client1.SendAsync(nonExistentReq);
        Assert.Equal(HttpStatusCode.NotFound, nonExistentResp.StatusCode);

        // Verify in database: Order 1 status remains PENDING_PAYMENT and seat remains HELD
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var dbOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == order1.OrderId);
        Assert.Equal(OrderStates.Pending, dbOrder.Status);

        var dbSeat = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtimeId && s.SeatId == seat.SeatId);
        Assert.Equal(SeatStates.Held, dbSeat.State);
        Assert.Equal(order1.OrderId, dbSeat.OwnerOrderId);
    }

    [Fact]
    public async Task TC_WEB_BOOK_12_CancelPendingOrder_Succeeds_MarksCancelledAndReleasesSeats()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var reserveReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        reserveReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        reserveReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var reserveResp = await client.SendAsync(reserveReq);
        var order = await reserveResp.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order);

        // Cancel order
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/cancel");
        cancelReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        cancelReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var cancelResp = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        // Verify in database
        await using var checkDb = await fixture.CreateDbContextAsync();
        var dbOrder = await checkDb.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal("CANCELLED", dbOrder.Status);

        var dbSeat = await checkDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtimeId && s.SeatId == seat.SeatId);
        Assert.Equal(SeatStates.Available, dbSeat.State);
        Assert.Null(dbSeat.OwnerOrderId);
    }

    [Fact]
    public async Task TC_WEB_BOOK_13_CancelPaidOrder_Returns422UnprocessableEntity()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var customer = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var reserveReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        reserveReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        reserveReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var reserveResp = await client.SendAsync(reserveReq);
        var order = await reserveResp.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order);

        // Perform legitimate payment via PaymentService and mock payment security
        await using var payDb = await fixture.CreateDbContextAsync();
        var paymentService = new PaymentService(payDb, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customer.UserId, order.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-" + Guid.NewGuid().ToString("N"), order.OrderCode, order.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));
        var payResult = await paymentService.ApplyVerifiedMockAsync(verified);
        Assert.Equal("APPLIED", payResult.Decision);
        Assert.Equal("PAID", payResult.OrderStatus);

        // Verify initial state: PAID, SOLD, ISSUED
        await using var preCancelDb = await fixture.CreateDbContextAsync();
        var paidOrder = await preCancelDb.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStates.Paid, paidOrder.Status);
        Assert.NotNull(paidOrder.PaidUtc);

        var soldSeat = await preCancelDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtimeId && s.SeatId == seat.SeatId);
        Assert.Equal(SeatStates.Sold, soldSeat.State);

        var ticket = await preCancelDb.Tickets.SingleAsync(t => t.OrderId == order.OrderId);
        Assert.Equal("ISSUED", ticket.Status);

        // Customer attempts to cancel PAID order via API -> Must return 422 UnprocessableEntity
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/cancel");
        cancelReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        cancelReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var cancelResp = await client.SendAsync(cancelReq);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, cancelResp.StatusCode);

        // Verify database: PAID order remains untouched, seat remains SOLD, ticket remains ISSUED
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var dbOrderAfter = await verifyDb.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStates.Paid, dbOrderAfter.Status);
        Assert.NotNull(dbOrderAfter.PaidUtc);

        var dbSeatAfter = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtimeId && s.SeatId == seat.SeatId);
        Assert.Equal(SeatStates.Sold, dbSeatAfter.State);
        Assert.Equal(order.OrderId, dbSeatAfter.OwnerOrderId);

        var dbTicketAfter = await verifyDb.Tickets.SingleAsync(t => t.OrderId == order.OrderId);
        Assert.Equal("ISSUED", dbTicketAfter.Status);

        // Integrity invariant check: No cancelled/expired/failed orders hold any HELD seats
        await verifyDb.Database.OpenConnectionAsync();
        await using var cmd = verifyDb.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT_BIG(*) FROM dbo.ShowtimeSeats ss JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='HELD' AND o.status IN ('CANCELLED','EXPIRED','FAILED')";
        var deadHolds = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, deadHolds);
    }

    [Fact]
    public async Task TC_WEB_BOOK_14_CancelExpiredOrder_TransitionsToEXPIRED_Returns409Conflict()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var reserveReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        reserveReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        reserveReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var reserveResp = await client.SendAsync(reserveReq);
        var order = await reserveResp.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order);

        // Fast-forward order expiration time to 10 minutes in the past
        await using var updateDb = await fixture.CreateDbContextAsync();
        var dbOrder = await updateDb.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        dbOrder.CreatedUtc = DateTime.UtcNow.AddMinutes(-15);
        dbOrder.ExpiresUtc = DateTime.UtcNow.AddMinutes(-10);
        await updateDb.SaveChangesAsync();

        // Attempt cancel
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/cancel");
        cancelReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        cancelReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var cancelResp = await client.SendAsync(cancelReq);

        Assert.Equal(HttpStatusCode.Conflict, cancelResp.StatusCode);

        // Verify status in DB became EXPIRED and seat was freed
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var expiredOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStates.Expired, expiredOrder.Status);

        var freedSeat = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtimeId && s.SeatId == seat.SeatId);
        Assert.Equal(SeatStates.Available, freedSeat.State);
        Assert.Null(freedSeat.OwnerOrderId);
    }

    [Fact]
    public async Task TC_WEB_BOOK_15_GetOrder_IsReadOnly_DoesNotMutateDatabase()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var reserveReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        reserveReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        reserveReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var reserveResp = await client.SendAsync(reserveReq);
        var order = await reserveResp.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order);

        await using var readDb1 = await fixture.CreateDbContextAsync();
        var before = await readDb1.Orders.AsNoTracking().SingleAsync(o => o.OrderId == order.OrderId);

        // Call GET order multiple times
        for (int i = 0; i < 3; i++)
        {
            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.OrderId}");
            getReq.Headers.Add("Cookie", auth);
            var getResp = await client.SendAsync(getReq);
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        }

        await using var readDb2 = await fixture.CreateDbContextAsync();
        var after = await readDb2.Orders.AsNoTracking().SingleAsync(o => o.OrderId == order.OrderId);

        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CreatedUtc, after.CreatedUtc);
        Assert.Equal(before.ExpiresUtc, after.ExpiresUtc);
        Assert.Equal(before.RowVer, after.RowVer);
    }

    [Fact]
    public async Task TC_WEB_BOOK_16_PostReserve_WithoutAntiforgeryToken_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (auth, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        // Missing X-XSRF-TOKEN header and form token
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = 1, SeatIds = [1] })
        };
        req.Headers.Add("Cookie", auth);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_BOOK_17_OrderTotalVnd_MatchesOrderSeatsSnapshot()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).Take(2).Select(s => s.SeatId).ToListAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var reserveReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = seats })
        };
        reserveReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        reserveReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var reserveResp = await client.SendAsync(reserveReq);
        var order = await reserveResp.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order);

        await using var verifyDb = await fixture.CreateDbContextAsync();
        var orderSeats = await verifyDb.OrderSeats.Where(os => os.OrderId == order.OrderId).ToListAsync();
        var expectedTotal = orderSeats.Sum(os => os.UnitPriceVnd);

        Assert.Equal(expectedTotal, order.TotalVnd);
    }

    [Fact]
    public async Task TC_WEB_BOOK_18_MvcSeatSelectionFlow_RedirectsToOrderDetails()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["showtimeId"] = showtimeId.ToString(),
            ["seatIdsCsv"] = seat.SeatId.ToString()
        };

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/bookings/reserve")
        {
            Content = new FormUrlEncodedContent(form)
        };
        postReq.Headers.Add("Cookie", $"{auth}; {afCookie}");

        var resp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/orders/", resp.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TC_WEB_BOOK_19_GetMyOrders_PaginationAndList_ReturnsAccurateOrders()
    {
        var client = CreateNewClient();
        var (auth, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/orders?page=1&pageSize=5");
        req.Headers.Add("Cookie", auth);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(jsonDoc.RootElement.TryGetProperty("orders", out var ordersProp));
        Assert.True(jsonDoc.RootElement.TryGetProperty("totalCount", out _));
        Assert.Equal(JsonValueKind.Array, ordersProp.ValueKind);
    }

    [Fact]
    public async Task TC_WEB_BOOK_20_OrderLifecycleService_ExpireDueAsync_CleansUpExpiredOrders()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var customer = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var resService = new SeatReservationService(db, new SystemClock());
        var res = await resService.ReserveAsync(customer.UserId, showtimeId, [seat.SeatId]);

        // Fast-forward expired order
        await using var updateDb = await fixture.CreateDbContextAsync();
        var order = await updateDb.Orders.SingleAsync(o => o.OrderId == res.OrderId);
        order.CreatedUtc = DateTime.UtcNow.AddMinutes(-15);
        order.ExpiresUtc = DateTime.UtcNow.AddMinutes(-10);
        await updateDb.SaveChangesAsync();

        // Expire due
        await using var lifecycleDb = await fixture.CreateDbContextAsync();
        var lifecycle = new OrderLifecycleService(lifecycleDb, new SystemClock());
        var expiredCount = await lifecycle.ExpireDueAsync();

        Assert.True(expiredCount >= 1);

        await using var verifyDb = await fixture.CreateDbContextAsync();
        var verifiedOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == res.OrderId);
        Assert.Equal(OrderStates.Expired, verifiedOrder.Status);
    }

    [Theory]
    [InlineData("1,1")] // Duplicate
    [InlineData("1,abc")] // Non-numeric
    [InlineData("0,2")] // Zero/Negative
    [InlineData("1,,2")] // Empty token
    [InlineData("")] // Empty string
    [InlineData("1,2,3,4,5,6,7,8,9")] // More than 8 seats
    public async Task TC_WEB_BOOK_21_MvcReserve_InvalidOrDuplicateSeatInput_RejectsWithFlashError(string invalidSeatCsv)
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(10);
        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["showtimeId"] = showtimeId.ToString(),
            ["seatIdsCsv"] = invalidSeatCsv
        };

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/bookings/reserve")
        {
            Content = new FormUrlEncodedContent(form)
        };
        postReq.Headers.Add("Cookie", $"{auth}; {afCookie}");

        var resp = await client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains($"/showtimes/{showtimeId}/seats", resp.Headers.Location?.OriginalString);

        // Ensure zero orders created in database for this showtime
        await using var checkDb = await fixture.CreateDbContextAsync();
        var ordersCount = await checkDb.Orders.CountAsync(o => o.ShowtimeId == showtimeId);
        Assert.Equal(0, ordersCount);
    }

    [Fact]
    public async Task TC_WEB_BOOK_22_ReserveShowtimePastOrNotOnSale_Rejects()
    {
        // 1. Create showtime in the past
        await using var db = await fixture.CreateDbContextAsync();
        var movie = await db.Movies.FirstAsync();
        var room = await db.Rooms.FirstAsync();
        var manager = await db.Users.FirstAsync(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID");

        var pastShowtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = room.RoomId,
            StartUtc = DateTime.UtcNow.AddMinutes(-30),
            EndUtc = DateTime.UtcNow.AddMinutes(90),
            BufferMinutes = 15,
            Status = "ON_SALE",
            SellableCapacitySnapshot = 5,
            CreatedBy = manager.UserId,
            CreatedUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.Showtimes.Add(pastShowtime);
        await db.SaveChangesAsync();

        var seat = await db.Seats.Where(s => s.RoomId == room.RoomId && s.IsActive).FirstAsync();
        db.ShowtimeSeats.Add(new ShowtimeSeat
        {
            ShowtimeId = pastShowtime.ShowtimeId,
            SeatId = seat.SeatId,
            RoomId = room.RoomId,
            State = SeatStates.Available
        });
        foreach (var st in await db.SeatTypes.ToListAsync())
        {
            db.SeatPrices.Add(new SeatPrice
            {
                ShowtimeId = pastShowtime.ShowtimeId,
                SeatTypeCode = st.SeatTypeCode,
                AmountVnd = 70000,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        // GET seats map: Must indicate showtime is not open for booking
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/showtimes/{pastShowtime.ShowtimeId}/seats");
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var seatMap = await getResp.Content.ReadFromJsonAsync<SeatMapDto>();
        Assert.NotNull(seatMap);
        Assert.False(seatMap.IsOpenForBooking);
        Assert.NotEmpty(seatMap.IneligibleReason!);
        Assert.All(seatMap.Seats, s => Assert.False(s.IsBookable));

        // POST reserve via API: Must return 409 Conflict
        var apiReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = pastShowtime.ShowtimeId, SeatIds = [seat.SeatId] })
        };
        apiReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        apiReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var apiResp = await client.SendAsync(apiReq);
        Assert.Equal(HttpStatusCode.Conflict, apiResp.StatusCode);

        // POST reserve via MVC: Must redirect to Seats with error flash message
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["showtimeId"] = pastShowtime.ShowtimeId.ToString(),
            ["seatIdsCsv"] = seat.SeatId.ToString()
        };
        var mvcReq = new HttpRequestMessage(HttpMethod.Post, "/bookings/reserve")
        {
            Content = new FormUrlEncodedContent(form)
        };
        mvcReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        var mvcResp = await client.SendAsync(mvcReq);
        Assert.Equal(HttpStatusCode.Redirect, mvcResp.StatusCode);
        Assert.Contains($"/showtimes/{pastShowtime.ShowtimeId}/seats", mvcResp.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TC_WEB_BOOK_23_ConcurrentCancelAndPayment_MaintainsConsistency()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var customer = await db.Users.FirstAsync(u => u.NormalizedEmail == "CUSTOMER1@DEMO.INVALID");
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var resService = new SeatReservationService(db, new SystemClock());
        var reservation = await resService.ReserveAsync(customer.UserId, showtimeId, [seat.SeatId]);

        // Setup payment attempt
        await using var paySetupDb = await fixture.CreateDbContextAsync();
        var paymentService = new PaymentService(paySetupDb, new SystemClock(), new QrTokenProtector());
        var attemptKey = Guid.NewGuid().ToString("N");
        await paymentService.CreateMockAttemptAsync(customer.UserId, reservation.OrderId, attemptKey);

        var msg = new MockPaymentMessage(Guid.NewGuid().ToString("N"), attemptKey, "TXN-" + Guid.NewGuid().ToString("N"), reservation.OrderCode, reservation.TotalVnd, "SUCCESS");
        var verified = MockPaymentSecurity.Verify(msg, MockPaymentSecurity.SignForDevelopment(msg));

        // Fire Cancel and Payment concurrently
        var cancelTask = Task.Run(async () =>
        {
            await using var cancelDb = await fixture.CreateDbContextAsync();
            var lifecycle = new OrderLifecycleService(cancelDb, new SystemClock());
            try
            {
                return await lifecycle.CancelAsync(customer.UserId, reservation.OrderId);
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        });

        var paymentTask = Task.Run(async () =>
        {
            await using var applyDb = await fixture.CreateDbContextAsync();
            var pay = new PaymentService(applyDb, new SystemClock(), new QrTokenProtector());
            try
            {
                return await pay.ApplyVerifiedMockAsync(verified);
            }
            catch (BusinessRuleException)
            {
                return null;
            }
        });

        await Task.WhenAll(cancelTask, paymentTask);

        // Verification of invariant consistency
        await using var verifyDb = await fixture.CreateDbContextAsync();
        var finalOrder = await verifyDb.Orders.SingleAsync(o => o.OrderId == reservation.OrderId);
        var finalSeat = await verifyDb.ShowtimeSeats.SingleAsync(s => s.ShowtimeId == showtimeId && s.SeatId == seat.SeatId);
        var tickets = await verifyDb.Tickets.Where(t => t.OrderId == reservation.OrderId).ToListAsync();

        if (finalOrder.Status == OrderStates.Paid)
        {
            Assert.Equal(SeatStates.Sold, finalSeat.State);
            Assert.Single(tickets);
            Assert.Equal("ISSUED", tickets[0].Status);
        }
        else
        {
            Assert.Equal(OrderStates.Cancelled, finalOrder.Status);
            Assert.Equal(SeatStates.Available, finalSeat.State);
            Assert.Empty(tickets);
        }
    }

    [Fact]
    public async Task TC_WEB_BOOK_24_PostCancel_WithoutAntiforgeryToken_Returns400BadRequest()
    {
        var showtimeId = await CreateIsolatedShowtimeAsync(5);
        await using var db = await fixture.CreateDbContextAsync();
        var seat = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).FirstAsync();

        var client = CreateNewClient();
        var (auth, afCookie, afToken) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var reserveReq = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/reserve")
        {
            Content = JsonContent.Create(new ReserveSeatsRequest { ShowtimeId = showtimeId, SeatIds = [seat.SeatId] })
        };
        reserveReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        reserveReq.Headers.Add("X-XSRF-TOKEN", afToken);
        var reserveResp = await client.SendAsync(reserveReq);
        var order = await reserveResp.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(order);

        // Send cancel request with auth cookie but MISSING X-XSRF-TOKEN header
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.OrderId}/cancel");
        cancelReq.Headers.Add("Cookie", $"{auth}; {afCookie}");
        // Intentionally omit X-XSRF-TOKEN header

        var cancelResp = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.BadRequest, cancelResp.StatusCode);
    }
}
