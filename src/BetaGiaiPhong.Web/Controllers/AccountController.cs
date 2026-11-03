using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BetaGiaiPhong.Web.Models.Requests;
using BetaGiaiPhong.Web.Models.ViewModels;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Controllers;

[Route("account")]
public sealed class AccountController(IAuthService authService, IWebHostEnvironment env) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        var vm = new LoginViewModel
        {
            Request = new LoginRequest { ReturnUrl = returnUrl },
            IsDevelopment = env.IsDevelopment()
        };

        return View(vm);
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("LoginRateLimit")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        model.IsDevelopment = env.IsDevelopment();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await authService.LoginAsync(
            model.Request.Email,
            model.Request.Password,
            model.Request.RememberMe,
            HttpContext,
            ct);

        if (!result.Succeeded)
        {
            model.ErrorMessage = result.ErrorMessage ?? "Email hoặc mật khẩu không chính xác.";
            return View(model);
        }

        if (!string.IsNullOrEmpty(model.Request.ReturnUrl) && Url.IsLocalUrl(model.Request.ReturnUrl))
        {
            return Redirect(model.Request.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await authService.LogoutAsync(HttpContext);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        var profile = await authService.GetUserProfileAsync(userId, ct);
        if (profile is null)
        {
            return NotFound("Thông tin người dùng không tồn tại.");
        }

        return View(profile);
    }
}
