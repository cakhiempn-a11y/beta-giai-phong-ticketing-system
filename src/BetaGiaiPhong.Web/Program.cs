using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using BetaGiaiPhong.Data;
using BetaGiaiPhong.Web.Middleware;
using BetaGiaiPhong.Web.Services;
using BetaGiaiPhong.Web.Services.Workers;

var builder = WebApplication.CreateBuilder(args);

// 0. Cryptographic keys resolution and validation
var qrKey = builder.Configuration["BGP_QR_AES_KEY_B64"]
    ?? Environment.GetEnvironmentVariable("BGP_QR_AES_KEY_B64");

if (string.IsNullOrWhiteSpace(qrKey))
{
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "Cấu hình BGP_QR_AES_KEY_B64 bị thiếu. Vui lòng cung cấp chuỗi Base64 của 32 byte ngẫu nhiên qua .NET User Secrets hoặc biến môi trường.");
    }
}
else
{
    try
    {
        var rawAes = Convert.FromBase64String(qrKey);
        if (rawAes.Length != 32)
        {
            throw new InvalidOperationException(
                $"Khóa BGP_QR_AES_KEY_B64 phải có độ dài giải mã chính xác 32 byte (nhận được {rawAes.Length} byte).");
        }
        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", qrKey);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("BGP_QR_AES_KEY_B64 không phải là chuỗi Base64 hợp lệ.");
    }
}

var hmacKey = builder.Configuration["BGP_MOCK_HMAC_KEY_B64"]
    ?? Environment.GetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64");

if (string.IsNullOrWhiteSpace(hmacKey))
{
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "Cấu hình BGP_MOCK_HMAC_KEY_B64 bị thiếu. Vui lòng cung cấp chuỗi Base64 của tối thiểu 32 byte ngẫu nhiên qua .NET User Secrets hoặc biến môi trường.");
    }
}
else
{
    try
    {
        var rawHmac = Convert.FromBase64String(hmacKey);
        if (rawHmac.Length < 32)
        {
            throw new InvalidOperationException(
                $"Khóa BGP_MOCK_HMAC_KEY_B64 phải có độ dài giải mã tối thiểu 32 byte (nhận được {rawHmac.Length} byte).");
        }
        Environment.SetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64", hmacKey);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("BGP_MOCK_HMAC_KEY_B64 không phải là chuỗi Base64 hợp lệ.");
    }
}

// 1. Connection string resolution
var connectionString = builder.Configuration.GetConnectionString("BetaDb")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__BetaDb")
    ?? "Server=localhost;Database=BetaGiaiPhong_Dev;Trusted_Connection=True;TrustServerCertificate=True;";

// 2. Data Layer registration
builder.Services.AddBetaCinemaData(connectionString);

// 3. Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IBookingQueryService, BookingQueryService>();
builder.Services.AddScoped<ITicketQueryService, TicketQueryService>();
builder.Services.AddHostedService<ReservationExpiryWorker>();

// 4. Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".BetaGiaiPhong.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/access-denied";
        options.ReturnUrlParameter = "returnUrl";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") ||
                (context.Request.Path.StartsWithSegments("/tickets") && context.Request.Path.Value?.EndsWith("/qr") == true))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") ||
                (context.Request.Path.StartsWithSegments("/tickets") && context.Request.Path.Value?.EndsWith("/qr") == true))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

// 5. Role-based Authorization Policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireCustomer", policy => policy.RequireRole("CUSTOMER"))
    .AddPolicy("RequireStaff", policy => policy.RequireRole("STAFF", "MANAGER", "ADMIN"))
    .AddPolicy("RequireManager", policy => policy.RequireRole("MANAGER", "ADMIN"))
    .AddPolicy("RequireAdmin", policy => policy.RequireRole("ADMIN"));

// 6. Anti-forgery
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = ".BetaGiaiPhong.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// 7. Rate Limiting for security sensitive routes
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("LoginRateLimit", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// 8. MVC and API controllers
builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

// 9. Middleware Pipeline
app.UseMiddleware<CorrelationIdMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseStatusCodePagesWithReExecute("/error", "?statusCode={0}");

// Basic security response headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Provide XSRF-TOKEN cookie for JavaScript fetch / AJAX clients
app.Use(async (context, next) =>
{
    var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    if (!string.IsNullOrEmpty(tokens.RequestToken))
    {
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken,
            new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Lax, Path = "/" });
    }
    await next();
});

app.MapControllers();
app.MapDefaultControllerRoute();
app.MapHealthChecks("/health");

app.Run();

// Required for WebApplicationFactory<Program> in test project
public partial class Program { }
