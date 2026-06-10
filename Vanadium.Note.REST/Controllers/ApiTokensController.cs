using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Controllers;

/// <summary>
/// Management of personal access tokens. These endpoints are restricted to the
/// interactive JWT scheme so that a token cannot be used to mint or enumerate other
/// tokens.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ApiController]
[Route("api/[controller]")]
public class ApiTokensController(ApiTokenService tokenService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiTokenSummary>>> GetAll(CancellationToken ct)
    {
        var tokens = await tokenService.ListAsync(UserId, ct);
        return Ok(tokens.Select(t => new ApiTokenSummary
        {
            Id = t.Id,
            Name = t.Name,
            TokenSuffix = t.TokenSuffix,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            LastUsedAt = t.LastUsedAt
        }));
    }

    [HttpPost]
    public async Task<ActionResult<CreateApiTokenResponse>> Create(
        [FromBody] CreateApiTokenRequest request, CancellationToken ct)
    {
        var (token, plaintext) = await tokenService.CreateAsync(
            UserId, request.Name, request.ExpiresInDays, ct);

        return Ok(new CreateApiTokenResponse
        {
            Id = token.Id,
            Name = token.Name,
            Token = plaintext,
            CreatedAt = token.CreatedAt,
            ExpiresAt = token.ExpiresAt
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await tokenService.DeleteAsync(UserId, id, ct);
        return deleted ? NoContent() : NotFound();
    }
}
