using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Fixed principal name for the single owner (used for logs/UI only).</summary>
    public const string OwnerName = "owner";

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        logger.LogInformation("Login attempt.");

        var storedHash = config["Auth:PasswordHash"];
        if (string.IsNullOrEmpty(storedHash))
        {
            logger.LogError("Auth:PasswordHash is not configured — login is impossible until it is set.");
            return Problem(
                detail: "Server password is not configured.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (!VerifyPassword(request.Password, storedHash))
        {
            logger.LogWarning("Failed login attempt.");
            return Unauthorized(new { message = "Invalid password." });
        }

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

        return Ok(new { hash = HashPassword(request.Password) });
    }

    private string GenerateJwtToken()
    {
        var secret = config["Auth:JwtSecret"]
            ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");
        var expirationMinutes = config.GetValue<int>("Auth:JwtExpirationMinutes", 1440);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Name, OwnerName)],
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: 32);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }
}
