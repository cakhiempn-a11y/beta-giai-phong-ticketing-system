using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Web.Models.Responses;
using BetaGiaiPhong.Web.Services;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Route("api/me")]
[Produces("application/json")]
public sealed class MeApiController(IAuthService authService) : ControllerBase
{
    [HttpGet("")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfileDto>> GetCurrentUser(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        var profile = await authService.GetUserProfileAsync(userId, ct);
        if (profile is null)
        {
            return NotFound();
        }

        return Ok(profile);
    }
}
