using BetaGiaiPhong.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BetaGiaiPhong.Web.Api;

[ApiController]
[Authorize(Policy = "RequireManager")]
[Route("api/management/movies")]
public sealed class ManagementMoviesApiController(MovieContentSyncService syncService) : ControllerBase
{
    [HttpGet("external-lookup")]
    public async Task<ActionResult<ExternalMovieLookupResponse>> ExternalLookup(
        [FromQuery] string? query,
        CancellationToken ct = default)
    {
        var response = await syncService.LookupExternalMoviesAsync(query, ct);
        return Ok(response);
    }
}
