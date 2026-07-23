using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vanadium.Note.REST.Controllers;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Security;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Auth;

/// <summary>
/// Authenticates requests bearing a personal access token (PAT). The plaintext is
/// hashed and matched against <c>ApiTokens.TokenHash</c>; on success a principal
/// identical in shape to a JWT principal (a single Name claim) is produced, so
/// downstream controllers need no PAT-specific logic.
/// </summary>
public class ApiTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    NoteDbContext db,
    IApiTokenThrottle throttle)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "vanadium-pat";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var plaintext = authHeader["Bearer ".Length..].Trim();
        if (!plaintext.StartsWith(ApiTokenService.TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        // Validate EVEN during an active lockout, so a legitimate token is never rejected just
        // because an attacker's invalid attempts armed the shared lock — the throttle otherwise
        // becomes a denial-of-service against the owner (issue #291, mirroring the login fix).
        // A valid token clears the lock below; only invalid/expired tokens feed RegisterFailure.
        // The extra ApiTokens query this incurs while locked is a single indexed lookup.
        var hash = ApiTokenService.HashToken(plaintext);

        var record = await db.ApiTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (record is null)
        {
            throttle.RegisterFailure();
            Logger.LogWarning("Rejected unknown API token.");
            return AuthenticateResult.Fail("Invalid API token.");
        }

        if (record.ExpiresAt is { } expiry && expiry <= DateTime.UtcNow)
        {
            throttle.RegisterFailure();
            Logger.LogWarning("Rejected expired API token {TokenId}", record.Id);
            return AuthenticateResult.Fail("API token has expired.");
        }

        // A valid token clears any accumulated failures so a legitimate client is never
        // caught behind a lockout that an attacker's invalid attempts triggered.
        throttle.RegisterSuccess();

        // Record usage, but avoid a write on every single request.
        var now = DateTime.UtcNow;
        if (record.LastUsedAt is null || now - record.LastUsedAt.Value > TimeSpan.FromMinutes(1))
        {
            record.LastUsedAt = now;
            await db.SaveChangesAsync();
        }

        var claims = new[] { new Claim(ClaimTypes.Name, AuthController.OwnerName) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
