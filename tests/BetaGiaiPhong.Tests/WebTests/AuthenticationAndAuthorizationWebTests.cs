using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Tests.IntegrationTests;
using BetaGiaiPhong.Web.Models.Responses;
using Xunit;

namespace BetaGiaiPhong.Tests.WebTests;

[Collection("DatabaseCollection")]
public sealed class AuthenticationAndAuthorizationWebTests(TestDatabaseFixture fixture) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory = new(fixture);

    private HttpClient CreateNewClient()
    {
        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
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

    [Fact]
    public async Task TC_WEB_AUTH_01_ValidCredentials_SignsInAndSetsCookie()
    {
        var client = CreateNewClient();
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Request.Email"] = "customer1@demo.invalid",
            ["Request.Password"] = "DemoPassword123!",
            ["Request.RememberMe"] = "false"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("Cookie", afCookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString;
        Assert.True(location is "/" or "/home" or "/Home");

        var authCookie = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(".BetaGiaiPhong.Auth"));
        Assert.NotNull(authCookie);
        Assert.Contains("httponly", authCookie.ToLowerInvariant());
    }

    [Fact]
    public async Task TC_WEB_AUTH_02_InvalidPassword_ReturnsGenericErrorMessage_NoAuthCookie()
    {
        var client = CreateNewClient();
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Request.Email"] = "customer1@demo.invalid",
            ["Request.Password"] = "WrongPassword999!",
            ["Request.RememberMe"] = "false"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("Cookie", afCookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("Email hoặc mật khẩu không chính xác", decodedHtml);

        // Verify NO auth cookie was issued
        var hasAuthCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
                            cookies.Any(c => c.Contains(".BetaGiaiPhong.Auth="));
        Assert.False(hasAuthCookie);
    }

    [Fact]
    public async Task TC_WEB_AUTH_03_NonExistentEmail_ReturnsGenericErrorMessage_NoLeakage()
    {
        var client = CreateNewClient();
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Request.Email"] = "nosuchuser@demo.invalid",
            ["Request.Password"] = "DemoPassword123!",
            ["Request.RememberMe"] = "false"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("Cookie", afCookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("Email hoặc mật khẩu không chính xác", decodedHtml);
    }

    [Fact]
    public async Task TC_WEB_AUTH_04_InactiveUser_IsRejectedWithAccountLockedMessage()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var disabledMail = "disabled_user@demo.invalid";
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == disabledMail.ToUpperInvariant());
        if (user is null)
        {
            user = new AppUser
            {
                Email = disabledMail,
                NormalizedEmail = disabledMail.ToUpperInvariant(),
                DisplayName = "Disabled User",
                Status = "DISABLED",
                CreatedUtc = DateTime.UtcNow
            };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "DemoPassword123!");
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = CreateNewClient();
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Request.Email"] = disabledMail,
            ["Request.Password"] = "DemoPassword123!",
            ["Request.RememberMe"] = "false"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Add("Cookie", afCookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("khóa hoặc không hoạt động", decodedHtml);
    }

    [Fact]
    public async Task TC_WEB_AUTH_05_UnauthenticatedUser_AccessingProfile_RedirectsToLogin()
    {
        var client = CreateNewClient();
        var response = await client.GetAsync("/account/profile");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TC_WEB_AUTH_06_ApiMe_Unauthenticated_Returns401Unauthorized()
    {
        var client = CreateNewClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_AUTH_07_AuthenticatedSession_CanAccessProfileAndApiMe()
    {
        var client = CreateNewClient();

        // 1. Log in
        var (afCookie, afToken) = await ExtractAntiforgeryTokenAsync(client, "/account/login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = afToken,
            ["Request.Email"] = "customer1@demo.invalid",
            ["Request.Password"] = "DemoPassword123!"
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

        // 2. Call /api/me with auth cookie
        var apiReq = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        apiReq.Headers.Add("Cookie", authCookie);

        var apiResp = await client.SendAsync(apiReq);
        Assert.Equal(HttpStatusCode.OK, apiResp.StatusCode);

        var profile = await apiResp.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(profile);
        Assert.Equal("customer1@demo.invalid", profile.Email);
        Assert.Contains("CUSTOMER", profile.Roles);
    }
}
