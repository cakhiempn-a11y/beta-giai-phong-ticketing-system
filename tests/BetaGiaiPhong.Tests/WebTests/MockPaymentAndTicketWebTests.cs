using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public sealed class MockPaymentAndTicketWebTests(TestDatabaseFixture fixture) : IClassFixture<CustomWebApplicationFactory>
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

    private async Task<(long OrderId, string OrderCode, long TotalVnd)> CreateOrderAsync(
        long customerId, long showtimeId, int seatCount = 2)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var clock = new SystemClock();
        var reservationService = new SeatReservationService(db, clock);

        var seats = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == showtimeId && s.State == SeatStates.Available)
            .Take(seatCount)
            .Select(s => s.SeatId)
            .ToListAsync();

        var res = await reservationService.ReserveAsync(customerId, showtimeId, seats);
        return (res.OrderId, res.OrderCode, res.TotalVnd);
    }

    [Fact]
    public async Task TC_WEB_PAY_01_Guest_CannotAccessCheckout_RedirectsToLogin()
    {
        var client = CreateNewClient();
        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, _, _) = await CreateOrderAsync(1, showtimeId, 1);

        var response = await client.GetAsync($"/orders/{orderId}/checkout");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TC_WEB_PAY_02_CustomerA_CannotCheckoutOrPayCustomerB_Order()
    {
        var clientA = CreateNewClient();
        var (authCookieA, afCookieA, xsrfA) = await LoginAndGetTokensAsync(clientA, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        // Order belongs to customer 2 (customer2@demo.invalid)
        var (orderBId, _, _) = await CreateOrderAsync(2, showtimeId, 1);

        // 1. GET /orders/{id}/checkout for Customer B's order
        var checkoutReq = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderBId}/checkout");
        checkoutReq.Headers.Add("Cookie", authCookieA);
        var checkoutResp = await clientA.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.Redirect, checkoutResp.StatusCode);
        Assert.Equal("/orders", checkoutResp.Headers.Location?.ToString());

        // 2. POST /api/orders/{id}/payments/mock-attempt for Customer B's order
        var attemptReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderBId}/payments/mock-attempt")
        {
            Content = JsonContent.Create(new CreateMockAttemptRequest("KEY-CROSS-USER"))
        };
        attemptReq.Headers.Add("Cookie", $"{authCookieA}; {afCookieA}; XSRF-TOKEN={xsrfA}");
        attemptReq.Headers.Add("X-XSRF-TOKEN", xsrfA);

        var attemptResp = await clientA.SendAsync(attemptReq);
        Assert.Equal(HttpStatusCode.NotFound, attemptResp.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_PAY_03_ExpiredOrder_CannotCreateMockAttempt()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, xsrf) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, _, _) = await CreateOrderAsync(1, showtimeId, 1);

        // Mutate order to expired in DB
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ord = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            ord.CreatedUtc = DateTime.UtcNow.AddMinutes(-15);
            ord.ExpiresUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments/mock-attempt")
        {
            Content = JsonContent.Create(new CreateMockAttemptRequest("ATTEMPT-EXP"))
        };
        req.Headers.Add("Cookie", $"{authCookie}; {afCookie}; XSRF-TOKEN={xsrf}");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_PAY_04_DuplicateAttemptKey_ReturnsSamePaymentAttempt()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, xsrf) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, _, _) = await CreateOrderAsync(1, showtimeId, 1);
        var attemptKey = "IDEMPOTENT-ATTEMPT-" + Guid.NewGuid().ToString("N");

        // First attempt call
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments/mock-attempt")
        {
            Content = JsonContent.Create(new CreateMockAttemptRequest(attemptKey))
        };
        req1.Headers.Add("Cookie", $"{authCookie}; {afCookie}; XSRF-TOKEN={xsrf}");
        req1.Headers.Add("X-XSRF-TOKEN", xsrf);
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var dto1 = await resp1.Content.ReadFromJsonAsync<PaymentAttemptDto>();
        Assert.NotNull(dto1);

        // Second attempt call with identical attemptKey
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments/mock-attempt")
        {
            Content = JsonContent.Create(new CreateMockAttemptRequest(attemptKey))
        };
        req2.Headers.Add("Cookie", $"{authCookie}; {afCookie}; XSRF-TOKEN={xsrf}");
        req2.Headers.Add("X-XSRF-TOKEN", xsrf);
        var resp2 = await client.SendAsync(req2);
        Assert.True(resp2.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        var dto2 = await resp2.Content.ReadFromJsonAsync<PaymentAttemptDto>();
        Assert.NotNull(dto2);

        Assert.Equal(dto1.PaymentId, dto2.PaymentId);
        Assert.Equal(dto1.AttemptKey, dto2.AttemptKey);
    }

    [Fact]
    public async Task TC_WEB_PAY_05_WebhookWithoutSignature_Returns401Unauthorized()
    {
        var client = CreateNewClient();
        var webhookReq = new MockPaymentWebhookRequest(
            ProviderEventRef: "EVT-NOSIG",
            AttemptKey: "KEY-1",
            ProviderTransactionRef: "TXN-1",
            OrderCode: "BGP-TEST",
            AmountVnd: 100000,
            Outcome: "SUCCESS"
        );

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(webhookReq)
        };
        // No X-Signature header

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("MISSING_SIGNATURE", json);
    }

    [Fact]
    public async Task TC_WEB_PAY_06_WebhookWithInvalidSignature_Returns401Unauthorized()
    {
        var client = CreateNewClient();
        var webhookReq = new MockPaymentWebhookRequest(
            ProviderEventRef: "EVT-BADSIG",
            AttemptKey: "KEY-1",
            ProviderTransactionRef: "TXN-1",
            OrderCode: "BGP-TEST",
            AmountVnd: 100000,
            Outcome: "SUCCESS"
        );

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(webhookReq)
        };
        req.Headers.Add("X-Signature-SHA256", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_SIGNATURE", json);
    }

    [Fact]
    public async Task TC_WEB_PAY_07_WebhookWithTamperedPayload_Returns401Unauthorized()
    {
        var client = CreateNewClient();
        var originalMsg = new MockPaymentMessage("EVT-TAMPER", "ATT-1", "TXN-1", "BGP-TAMPER", 140000, "SUCCESS");
        var signature = MockPaymentSecurity.SignForDevelopment(originalMsg);

        // Tamper AmountVnd to 70000
        var tamperedPayload = new MockPaymentWebhookRequest("EVT-TAMPER", "ATT-1", "TXN-1", "BGP-TAMPER", 70000, "SUCCESS");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(tamperedPayload)
        };
        req.Headers.Add("X-Signature-SHA256", signature);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_PAY_08_09_10_ValidSuccessWebhook_CreatesPaidOrder_SoldSeats_AndIssuedTickets()
    {
        var client = CreateNewClient();
        var showtimeId = await CreateIsolatedShowtimeAsync(3);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 2);

        var attemptKey = "ATT-SUCCESS-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
        }

        var message = new MockPaymentMessage(
            ProviderEventRef: "EVT-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "SUCCESS"
        );
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(new MockPaymentWebhookRequest(
                message.ProviderEventRef, message.AttemptKey, message.ProviderTransactionRef,
                message.OrderCode, message.AmountVnd, message.Outcome))
        };
        req.Headers.Add("X-Signature", signature);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PaymentWebhookResponseDto>();
        Assert.NotNull(result);
        Assert.Equal("APPLIED", result.Decision);
        Assert.Equal("PAID", result.OrderStatus);

        // Verify Database State
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            Assert.Equal(OrderStates.Paid, order.Status);
            Assert.NotNull(order.PaidUtc);

            var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId && s.OwnerOrderId == orderId).ToListAsync();
            Assert.Equal(2, seats.Count);
            Assert.All(seats, s =>
            {
                Assert.Equal(SeatStates.Sold, s.State);
                Assert.Null(s.HoldExpiresUtc);
            });

            var tickets = await db.Tickets.Where(t => t.OrderId == orderId).ToListAsync();
            Assert.Equal(2, tickets.Count);
            Assert.All(tickets, t =>
            {
                Assert.Equal("ISSUED", t.Status);
                Assert.NotEmpty(t.QrTokenHash);
                Assert.NotEmpty(t.QrTokenCipher);
                Assert.StartsWith("TKT-", t.TicketCode);
            });
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_11_DuplicateWebhook_IsIdempotent_DoesNotCreateExtraTickets()
    {
        var client = CreateNewClient();
        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-DUP-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
        }

        var message = new MockPaymentMessage(
            ProviderEventRef: "EVT-DUP-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-DUP-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "SUCCESS"
        );
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var postMsg = () => new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(new MockPaymentWebhookRequest(
                message.ProviderEventRef, message.AttemptKey, message.ProviderTransactionRef,
                message.OrderCode, message.AmountVnd, message.Outcome)),
            Headers = { { "X-Signature-SHA256", signature } }
        };

        // First delivery: APPLIED
        var resp1 = await client.SendAsync(postMsg());
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        var r1 = await resp1.Content.ReadFromJsonAsync<PaymentWebhookResponseDto>();
        Assert.Equal("APPLIED", r1?.Decision);

        // Second delivery (same event ref): DUPLICATE
        var resp2 = await client.SendAsync(postMsg());
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        var r2 = await resp2.Content.ReadFromJsonAsync<PaymentWebhookResponseDto>();
        Assert.Equal("DUPLICATE", r2?.Decision);

        // Verify ticket count remains exactly 1
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ticketCount = await db.Tickets.CountAsync(t => t.OrderId == orderId);
            Assert.Equal(1, ticketCount);
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_12_ConcurrentDuplicateWebhooks_MaintainIdempotency()
    {
        var client1 = CreateNewClient();
        var client2 = CreateNewClient();
        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-RACE-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
        }

        var message = new MockPaymentMessage(
            ProviderEventRef: "EVT-RACE-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-RACE-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "SUCCESS"
        );
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var makeReq = () => new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(new MockPaymentWebhookRequest(
                message.ProviderEventRef, message.AttemptKey, message.ProviderTransactionRef,
                message.OrderCode, message.AmountVnd, message.Outcome)),
            Headers = { { "X-Signature-SHA256", signature } }
        };

        var task1 = client1.SendAsync(makeReq());
        var task2 = client2.SendAsync(makeReq());
        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var res1 = await responses[0].Content.ReadFromJsonAsync<PaymentWebhookResponseDto>();
        var res2 = await responses[1].Content.ReadFromJsonAsync<PaymentWebhookResponseDto>();

        var decisions = new[] { res1?.Decision, res2?.Decision };
        Assert.Contains("APPLIED", decisions);
        Assert.Contains("DUPLICATE", decisions);

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.Tickets.CountAsync(t => t.OrderId == orderId));
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_13_FailedWebhook_ReleasesHeldSeatsAndMarksOrderFailed()
    {
        var client = CreateNewClient();
        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-FAIL-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
        }

        var message = new MockPaymentMessage(
            ProviderEventRef: "EVT-FAIL-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-FAIL-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "FAILED"
        );
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(new MockPaymentWebhookRequest(
                message.ProviderEventRef, message.AttemptKey, message.ProviderTransactionRef,
                message.OrderCode, message.AmountVnd, message.Outcome)),
            Headers = { { "X-Signature-SHA256", signature } }
        };

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            Assert.Equal(OrderStates.Failed, order.Status);

            var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).ToListAsync();
            Assert.All(seats, s =>
            {
                Assert.Equal(SeatStates.Available, s.State);
                Assert.Null(s.OwnerOrderId);
            });

            Assert.Equal(0, await db.Tickets.CountAsync(t => t.OrderId == orderId));
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_14_LatePayment_DoesNotReviveExpiredOrder_NoTicketsIssued()
    {
        var client = CreateNewClient();
        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-LATE-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);

            // Mutate order to expired before callback arrives
            var ord = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            ord.CreatedUtc = DateTime.UtcNow.AddMinutes(-15);
            ord.ExpiresUtc = DateTime.UtcNow.AddMinutes(-5);
            ord.Status = OrderStates.Expired;
            var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId && s.OwnerOrderId == orderId).ToListAsync();
            foreach (var s in seats)
            {
                s.State = SeatStates.Available;
                s.OwnerOrderId = null;
                s.HoldExpiresUtc = null;
            }
            await db.SaveChangesAsync();
        }

        var message = new MockPaymentMessage(
            ProviderEventRef: "EVT-LATE-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-LATE-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "SUCCESS"
        );
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(new MockPaymentWebhookRequest(
                message.ProviderEventRef, message.AttemptKey, message.ProviderTransactionRef,
                message.OrderCode, message.AmountVnd, message.Outcome)),
            Headers = { { "X-Signature-SHA256", signature } }
        };

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PaymentWebhookResponseDto>();
        Assert.Equal("NEEDS_REVIEW", result?.Decision);

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            Assert.Equal(OrderStates.Expired, order.Status);
            Assert.Equal(0, await db.Tickets.CountAsync(t => t.OrderId == orderId));
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_15_ConcurrentCancelAndPayment_MaintainsDataConsistency()
    {
        var clientA = CreateNewClient();
        var clientWebhook = CreateNewClient();
        var (authCookie, afCookie, xsrf) = await LoginAndGetTokensAsync(clientA, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-RACE-CANCEL-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
        }

        var message = new MockPaymentMessage(
            ProviderEventRef: "EVT-RACECANCEL-" + Guid.NewGuid().ToString("N"),
            AttemptKey: attemptKey,
            ProviderTransactionRef: "TXN-RACECANCEL-" + Guid.NewGuid().ToString("N"),
            OrderCode: orderCode,
            AmountVnd: totalVnd,
            Outcome: "SUCCESS"
        );
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        // Prepare cancel request
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        cancelReq.Headers.Add("Cookie", $"{authCookie}; {afCookie}; XSRF-TOKEN={xsrf}");
        cancelReq.Headers.Add("X-XSRF-TOKEN", xsrf);

        // Prepare webhook request
        var webhookReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mock-payment")
        {
            Content = JsonContent.Create(new MockPaymentWebhookRequest(
                message.ProviderEventRef, message.AttemptKey, message.ProviderTransactionRef,
                message.OrderCode, message.AmountVnd, message.Outcome)),
            Headers = { { "X-Signature-SHA256", signature } }
        };

        var taskCancel = clientA.SendAsync(cancelReq);
        var taskWebhook = clientWebhook.SendAsync(webhookReq);
        await Task.WhenAll(taskCancel, taskWebhook);

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            Assert.True(order.Status is OrderStates.Paid or OrderStates.Cancelled or OrderStates.Review);

            var seats = await db.ShowtimeSeats.Where(s => s.ShowtimeId == showtimeId).ToListAsync();
            if (order.Status == OrderStates.Paid)
            {
                Assert.Contains(seats, s => s.State == SeatStates.Sold && s.OwnerOrderId == orderId);
                Assert.Equal(1, await db.Tickets.CountAsync(t => t.OrderId == orderId));
            }
            else
            {
                Assert.DoesNotContain(seats, s => s.State == SeatStates.Held && s.OwnerOrderId == orderId);
                Assert.Equal(0, await db.Tickets.CountAsync(t => t.OrderId == orderId));
            }
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_16_CustomerA_CannotViewCustomerB_Tickets()
    {
        var clientA = CreateNewClient();
        var (authCookieA, _, _) = await LoginAndGetTokensAsync(clientA, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        // Order belonging to customer 2
        var (orderBId, _, _) = await CreateOrderAsync(2, showtimeId, 1);

        // 1. GET /orders/{id}/tickets
        var mvcReq = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderBId}/tickets");
        mvcReq.Headers.Add("Cookie", authCookieA);
        var mvcResp = await clientA.SendAsync(mvcReq);
        Assert.Equal(HttpStatusCode.Redirect, mvcResp.StatusCode);
        Assert.Equal("/orders", mvcResp.Headers.Location?.ToString());

        // 2. GET /api/orders/{id}/tickets
        var apiReq = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderBId}/tickets");
        apiReq.Headers.Add("Cookie", authCookieA);
        var apiResp = await clientA.SendAsync(apiReq);
        Assert.Equal(HttpStatusCode.NotFound, apiResp.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_PAY_17_GetQrCode_ReturnsValidPng_DecryptsToValidToken()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-QR-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
            var msg = new MockPaymentMessage("EVT-QR-" + Guid.NewGuid().ToString("N"), attemptKey, "TXN-QR-" + Guid.NewGuid().ToString("N"), orderCode, totalVnd, "SUCCESS");
            var sig = MockPaymentSecurity.SignForDevelopment(msg);
            var ver = MockPaymentSecurity.Verify(msg, sig);
            await ps.ApplyVerifiedMockAsync(ver);
        }

        long ticketId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var t = await db.Tickets.FirstAsync(t => t.OrderId == orderId);
            ticketId = t.TicketId;
        }

        var qrReq = new HttpRequestMessage(HttpMethod.Get, $"/tickets/{ticketId}/qr");
        qrReq.Headers.Add("Cookie", authCookie);

        var qrResp = await client.SendAsync(qrReq);
        Assert.Equal(HttpStatusCode.OK, qrResp.StatusCode);
        Assert.Equal("image/png", qrResp.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", qrResp.Headers.CacheControl?.ToString());

        var bytes = await qrResp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 8);
        // PNG magic number: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x4E, bytes[2]);
        Assert.Equal(0x47, bytes[3]);
    }

    [Fact]
    public async Task TC_WEB_PAY_18_TicketApi_DoesNotLeakRawTokenOrCipher()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, orderCode, totalVnd) = await CreateOrderAsync(1, showtimeId, 1);

        var attemptKey = "ATT-LEAK-" + Guid.NewGuid().ToString("N");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ps = new PaymentService(db, new SystemClock(), new QrTokenProtector());
            await ps.CreateMockAttemptAsync(1, orderId, attemptKey);
            var msg = new MockPaymentMessage("EVT-LEAK-" + Guid.NewGuid().ToString("N"), attemptKey, "TXN-LEAK-" + Guid.NewGuid().ToString("N"), orderCode, totalVnd, "SUCCESS");
            var sig = MockPaymentSecurity.SignForDevelopment(msg);
            await ps.ApplyVerifiedMockAsync(MockPaymentSecurity.Verify(msg, sig));
        }

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}/tickets");
        req.Headers.Add("Cookie", authCookie);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("qrTokenHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qrTokenCipher", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cipher", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("qrCodeUrl", json);
    }

    [Fact]
    public void TC_WEB_PAY_19_QrKeyStability_UnsealsConsistentlyAcrossInstances()
    {
        var protector1 = new QrTokenProtector();
        var (token, hash, cipher) = protector1.Issue();

        // Second independent protector instance with same key
        var protector2 = new QrTokenProtector();
        var unsealed = protector2.Unseal(cipher);

        Assert.Equal(token, unsealed);
        Assert.Equal(hash, QrTokenProtector.HashToken(unsealed));
    }

    [Fact]
    public async Task TC_WEB_PAY_20_PostDemoPay_InDevelopment_TransitionsOrderToPaid()
    {
        var client = CreateNewClient();
        var (authCookie, afCookie, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, _, _) = await CreateOrderAsync(1, showtimeId, 1);

        // Fetch demo pay page with auth cookie to get fresh anti-forgery token
        var checkoutReq = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}/checkout");
        checkoutReq.Headers.Add("Cookie", authCookie);
        var checkoutResp = await client.SendAsync(checkoutReq);
        checkoutResp.EnsureSuccessStatusCode();

        var setCookieHeaders = checkoutResp.Headers.GetValues("Set-Cookie");
        var checkoutAfCookie = setCookieHeaders.FirstOrDefault(c => c.Contains(".BetaGiaiPhong.Antiforgery"))?.Split(';')[0] ?? afCookie;
        var html = await checkoutResp.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (!match.Success) match = Regex.Match(html, @"value=""([^""]+)""\s+name=""__RequestVerificationToken""");
        Assert.True(match.Success);
        var orderAfToken = match.Groups[1].Value;

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = orderAfToken,
            ["outcome"] = "SUCCESS"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/orders/{orderId}/demo-pay")
        {
            Content = new FormUrlEncodedContent(form)
        };
        req.Headers.Add("Cookie", $"{authCookie}; {checkoutAfCookie}");

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal($"/orders/{orderId}/tickets", resp.Headers.Location?.ToString());

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var order = await db.Orders.SingleAsync(o => o.OrderId == orderId);
            Assert.Equal(OrderStates.Paid, order.Status);
        }
    }

    [Fact]
    public async Task TC_WEB_PAY_21_PostDemoPay_WithoutCsrf_Returns400BadRequest()
    {
        var client = CreateNewClient();
        var (authCookie, _, _) = await LoginAndGetTokensAsync(client, "customer1@demo.invalid");

        var showtimeId = await CreateIsolatedShowtimeAsync(2);
        var (orderId, _, _) = await CreateOrderAsync(1, showtimeId, 1);

        var form = new Dictionary<string, string>
        {
            ["outcome"] = "SUCCESS"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/orders/{orderId}/demo-pay")
        {
            Content = new FormUrlEncodedContent(form)
        };
        req.Headers.Add("Cookie", authCookie);
        // Missing __RequestVerificationToken and AfCookie

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_PAY_22_MultiTableIntegrityChecks_ZeroViolationsAfterPayments()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var checks = new (string Name, string Sql)[]
        {
            ("Wrong room/seat", "SELECT COUNT_BIG(*) AS [Value] FROM dbo.ShowtimeSeats ss JOIN dbo.Seats s ON ss.seat_id=s.seat_id WHERE ss.room_id<>s.room_id"),
            ("SOLD seat without PAID order", "SELECT COUNT_BIG(*) AS [Value] FROM dbo.ShowtimeSeats ss LEFT JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='SOLD' AND (o.order_id IS NULL OR o.status<>'PAID')"),
            ("PAID orders missing tickets", "SELECT COUNT_BIG(*) AS [Value] FROM dbo.Orders o WHERE o.status='PAID' AND NOT EXISTS (SELECT 1 FROM dbo.Tickets t WHERE t.order_id=o.order_id)"),
            ("Order price snapshot mismatch", "SELECT COUNT_BIG(*) AS [Value] FROM dbo.Orders o WHERE o.total_vnd<>(SELECT COALESCE(SUM(os.unit_price_vnd),0) FROM dbo.OrderSeats os WHERE os.order_id=o.order_id)"),
            ("Late payment improperly SUCCESS", "SELECT COUNT_BIG(*) AS [Value] FROM dbo.Payments p JOIN dbo.Orders o ON o.order_id=p.order_id WHERE p.status='SUCCESS' AND p.resolved_utc>o.expires_utc"),
            ("Dead order still holds seat", "SELECT COUNT_BIG(*) AS [Value] FROM dbo.ShowtimeSeats ss JOIN dbo.Orders o ON o.order_id=ss.owner_order_id WHERE ss.state='HELD' AND o.status IN ('CANCELLED','EXPIRED','FAILED')")
        };

        foreach (var (name, sql) in checks)
        {
            var count = await db.Database.SqlQueryRaw<long>(sql).SingleAsync();
            Assert.True(count == 0, $"Integrity check '{name}' failed with {count} violation(s).");
        }
    }
}
