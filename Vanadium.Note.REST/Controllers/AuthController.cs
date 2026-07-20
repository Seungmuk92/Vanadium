using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Security;

namespace Vanadium.Note.REST.Controllers;

/// <summary>
/// Single-user, password-only authentication. There is no user identity: the app
/// belongs to one owner whose password hash lives in configuration
/// (<c>Auth:PasswordHash</c>), mirroring how <c>Auth:JwtSecret</c> is supplied.
/// A successful login mints a JWT carrying only a fixed display-name claim.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IConfiguration config,
    IWebHostEnvironment env,
    IPasswordValidator passwordValidator,
    ILoginThrottle loginThrottle,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Fixed principal name for the single owner (used for logs/UI only).</summary>
    public const string OwnerName = "owner";

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        logger.LogInformation("Login attempt.");

        // Global backoff gate: unlike the per-IP fixed-window limiter, this counter is shared
        // across every source, so distributed IPs or forged X-Forwarded-For cannot slip past it.
        // Attempts arriving during an active lockout are rejected without touching the password.
        if (loginThrottle.IsLocked(out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            logger.LogWarning("Login rejected by global lockout; {Seconds}s remaining.", seconds);
            return Problem(
                detail: "Too many failed login attempts. Try again later.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var storedHash = config["Auth:PasswordHash"];
        if (string.IsNullOrEmpty(storedHash))
        {
            logger.LogError("Auth:PasswordHash is not configured — login is impossible until it is set.");
            return Problem(
                detail: "Server password is not configured.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (!PasswordHasher.Verify(request.Password, storedHash))
        {
            loginThrottle.RegisterFailure();
            logger.LogWarning("Failed login attempt.");
            return Problem(detail: "Invalid password.", statusCode: StatusCodes.Status401Unauthorized);
        }

        loginThrottle.RegisterSuccess();
        var token = GenerateJwtToken();
        logger.LogInformation("Owner authenticated successfully.");
        return Ok(new { token });
    }

    /// <summary>
    /// Development only: computes the storage hash for a given password so it can be
    /// pasted into <c>Auth:PasswordHash</c>. Nothing is persisted — this replaces the
    /// old user-provisioning <c>setup</c> endpoint. The password must satisfy the
    /// configured password policy; weak passwords are rejected with 400 before any
    /// hash is produced.
    /// </summary>
    [HttpPost("hash")]
    public async Task<IActionResult> Hash([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var validation = await passwordValidator.ValidateAsync(request.Password, cancellationToken);
        if (!validation.IsValid)
        {
            logger.LogInformation("Rejected a weak password submitted to /api/auth/hash.");
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["Password"] = [.. validation.Errors] })
            {
                Title = "Password does not meet the security policy.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(new { hash = PasswordHasher.Hash(request.Password) });
    }

    private string GenerateJwtToken()
    {
        var secret = config["Auth:JwtSecret"]
            ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");
        // Default kept intentionally short (8h). The JWT lives in browser localStorage and
        // cannot be revoked server-side (no refresh tokens by design), so the token lifetime
        // is the whole XSS-theft exposure window — see docs/plannings/jwt-lifetime-and-storage.md.
        var expirationMinutes = config.GetValue<int>("Auth:JwtExpirationMinutes", 480);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Name, OwnerName)],
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
