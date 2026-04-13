using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var username = config["Auth:Username"];
        var passwordHash = config["Auth:PasswordHash"];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordHash))
            return StatusCode(503, new { message = "Authentication is not configured. Use the /api/auth/setup endpoint to set a password first." });

        if (!request.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
            || !VerifyPassword(request.Password, passwordHash))
            return Unauthorized(new { message = "Invalid username or password." });

        var token = GenerateJwtToken();
        return Ok(new { token });
    }

    /// <summary>
    /// Development only: generates a password hash.
    /// Store the returned hash value in the Auth:PasswordHash configuration.
    /// </summary>
    [HttpPost("setup")]
    public IActionResult Setup([FromBody] SetupRequest request)
    {
        if (!env.IsDevelopment())
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Password is required." });

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
            claims: [new Claim(ClaimTypes.Name, config["Auth:Username"]!)],
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
