using System.Net;
using System.Net.Http.Json;
using BetaGiaiPhong.Tests.IntegrationTests;
using Xunit;

namespace BetaGiaiPhong.Tests.WebTests;

[Collection("DatabaseCollection")]
public sealed class WebSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory;
    private readonly HttpClient client;

    public WebSecurityTests(TestDatabaseFixture fixture)
    {
        factory = new CustomWebApplicationFactory(fixture);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task TC_WEB_SEC_01_NoSensitiveCredentialsLeakageInResponses()
    {
        var moviesResp = await client.GetAsync("/api/movies");
        moviesResp.EnsureSuccessStatusCode();
        var moviesContent = await moviesResp.Content.ReadAsStringAsync();

        Assert.DoesNotContain("PasswordHash", moviesContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NormalizedEmail", moviesContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qr_token_cipher", moviesContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qr_hash", moviesContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BGP_MOCK_HMAC_KEY_B64", moviesContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TC_WEB_SEC_02_PostWithoutAntiforgery_IsRejectedWithBadRequest()
    {
        // Attempt POST to login without antiforgery token
        var form = new Dictionary<string, string>
        {
            ["Request.Email"] = "customer1@demo.invalid",
            ["Request.Password"] = "DemoPassword123!"
        };

        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(form));

        // Anti-forgery middleware rejects request with 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TC_WEB_SEC_03_SecurityHeaders_ArePresentInAllResponses()
    {
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());

        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
    }

    [Fact]
    public async Task TC_WEB_SEC_04_CorrelationId_IsReturnedInResponseHeaders()
    {
        var customCorrelationId = Guid.NewGuid().ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/movies");
        request.Headers.Add("X-Correlation-ID", customCorrelationId);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.Equal(customCorrelationId, response.Headers.GetValues("X-Correlation-ID").First());
    }

    [Fact]
    public async Task TC_WEB_SEC_05_HealthCheckEndpoint_ReturnsHealthy()
    {
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", content);
    }
}
