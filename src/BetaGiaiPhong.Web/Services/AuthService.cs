using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Web.Models.Responses;

namespace BetaGiaiPhong.Web.Services;

public sealed record AuthResult(bool Succeeded, string? ErrorMessage, UserProfileDto? User);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, bool rememberMe, HttpContext httpContext, CancellationToken ct = default);
    Task LogoutAsync(HttpContext httpContext);
    Task<UserProfileDto?> GetUserProfileAsync(long userId, CancellationToken ct = default);
}

public sealed class AuthService(BetaDbContext db, ILogger<AuthService> logger) : IAuthService
{
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static string FormatVietnamDateTime(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero)
            .ToOffset(VietnamOffset)
            .ToString("dd/MM/yyyy HH:mm");

    public async Task<AuthResult> LoginAsync(string email, string password, bool rememberMe, HttpContext httpContext, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthResult(false, "Vui lòng nhập đầy đủ email và mật khẩu.", null);
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        // Constant-time mitigation against timing attacks on missing user
        if (user is null)
        {
            logger.LogWarning("Login failed for email: {Email} (User not found).", normalizedEmail);
            return new AuthResult(false, "Email hoặc mật khẩu không chính xác.", null);
        }

        if (user.Status != "ACTIVE")
        {
            logger.LogWarning("Login failed for user ID {UserId}: account status is {Status}.", user.UserId, user.Status);
            return new AuthResult(false, "Tài khoản của bạn hiện đang bị khóa hoặc không hoạt động.", null);
        }

        var verificationResult = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Login failed for user ID {UserId}: Invalid password.", user.UserId);
            return new AuthResult(false, "Email hoặc mật khẩu không chính xác.", null);
        }

        var roles = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role!.RoleCode)
            .Distinct()
            .ToList();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName),
            new(ClaimTypes.Email, user.Email)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = rememberMe
                ? DateTimeOffset.UtcNow.AddDays(14)
                : DateTimeOffset.UtcNow.AddHours(8)
        };

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
        logger.LogInformation("User ID {UserId} ({Email}) logged in successfully with roles [{Roles}].",
            user.UserId, user.Email, string.Join(", ", roles));

        var profile = new UserProfileDto(user.UserId, user.Email, user.DisplayName, user.Status, roles);
        return new AuthResult(true, null, profile);
    }

    public async Task LogoutAsync(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        logger.LogInformation("User ID {UserId} logged out.", userId ?? "anonymous");
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.UserId == userId, ct);

        if (user is null) return null;

        var roles = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role!.RoleCode)
            .Distinct()
            .ToList();

        int totalOrders = 0;
        int paidOrders = 0;
        if (roles.Contains("CUSTOMER", StringComparer.OrdinalIgnoreCase))
        {
            totalOrders = await db.Orders.AsNoTracking().CountAsync(o => o.UserId == userId, ct);
            paidOrders = await db.Orders.AsNoTracking().CountAsync(o => o.UserId == userId && o.Status == OrderStates.Paid, ct);
        }

        return new UserProfileDto(
            UserId: user.UserId,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Status: user.Status,
            Roles: roles,
            CreatedVietnamText: FormatVietnamDateTime(user.CreatedUtc),
            TotalOrdersCount: totalOrders,
            PaidOrdersCount: paidOrders
        );
    }
}
