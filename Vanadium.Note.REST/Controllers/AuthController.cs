using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IConfiguration config, IWebHostEnvironment env, NoteDbContext db, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        logger.LogInformation("Login attempt for user '{Username}'", request.Username);

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user is null || !VerifyPassword(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for user '{Username}'", request.Username);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var token = GenerateJwtToken(user.Username);
        logger.LogInformation("User '{Username}' authenticated successfully", user.Username);
        return Ok(new { token });
    }

    /// <summary>
    /// Development only: creates or updates a user in the database.
    /// </summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (existing is not null)
        {
            existing.PasswordHash = HashPassword(request.Password);
        }
        else
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Username = request.Username,
                PasswordHash = HashPassword(request.Password)
            });
        }

        await db.SaveChangesAsync();
        return Ok(new { message = $"User '{request.Username}' has been set up successfully." });
    }

    private string GenerateJwtToken(string username)
    {
        var secret = config["Auth:JwtSecret"]
            ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");
        var expirationMinutes = config.GetValue<int>("Auth:JwtExpirationMinutes", 1440);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Name, username)],
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
