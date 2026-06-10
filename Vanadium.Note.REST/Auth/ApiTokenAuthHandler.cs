using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Services;

namespace Vanadium.Note.REST.Auth;

/// <summary>
/// Authenticates requests bearing a personal access token (PAT). The plaintext is
/// hashed and matched against <c>ApiTokens.TokenHash</c>; on success a principal
/// identical in shape to a JWT principal (Name + NameIdentifier) is produced, so
/// downstream controllers need no PAT-specific logic.
/// </summary>
public class ApiTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    NoteDbContext db)
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

        var hash = ApiTokenService.HashToken(plaintext);

        var record = await db.ApiTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (record is null)
        {
            Logger.LogWarning("Rejected unknown API token (suffix {Suffix})",
                plaintext.Length >= 4 ? plaintext[^4..] : "????");
            return AuthenticateResult.Fail("Invalid API token.");
        }

        if (record.ExpiresAt is { } expiry && expiry <= DateTime.UtcNow)
        {
            Logger.LogWarning("Rejected expired API token {TokenId}", record.Id);
            return AuthenticateResult.Fail("API token has expired.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == record.UserId);
        if (user is null)
            return AuthenticateResult.Fail("Token owner no longer exists.");

        // Record usage, but avoid a write on every single request.
        var now = DateTime.UtcNow;
        if (record.LastUsedAt is null || now - record.LastUsedAt.Value > TimeSpan.FromMinutes(1))
        {
            record.LastUsedAt = now;
            await db.SaveChangesAsync();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
