using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class ApiTokenService(NoteDbContext db, ILogger<ApiTokenService> logger)
{
    /// <summary>Prefix that distinguishes a personal access token from a JWT.</summary>
    public const string TokenPrefix = "van_pat_";

    /// <summary>
    /// Computes the storage hash for a plaintext token. Tokens carry full random
    /// entropy, so a single SHA-256 (no salt, no work factor) is sufficient and fast
    /// enough to run on every authenticated request.
    /// </summary>
    public static string HashToken(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Creates a token and returns the one-time plaintext value alongside the persisted
    /// record. The plaintext is never stored and cannot be recovered later.
    /// </summary>
    public async Task<(ApiToken Token, string Plaintext)> CreateAsync(
        string name, int? expiresInDays, CancellationToken ct = default)
    {
        var randomPart = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        var plaintext = TokenPrefix + randomPart;

        var token = new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = name,
            TokenHash = HashToken(plaintext),
            TokenSuffix = plaintext[^4..],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresInDays is { } days ? DateTime.UtcNow.AddDays(days) : null
        };

        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("API token {TokenId} '{TokenName}' created", token.Id, token.Name);

        return (token, plaintext);
    }

    public async Task<List<ApiToken>> ListAsync(CancellationToken ct = default)
    {
        return await db.ApiTokens
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>Deletes a token. Returns false if it does not exist.</summary>
    public async Task<bool> DeleteAsync(Guid tokenId, CancellationToken ct = default)
    {
        var token = await db.ApiTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId, ct);
        if (token is null)
            return false;

        db.ApiTokens.Remove(token);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("API token {TokenId} revoked", tokenId);
        return true;
    }
}
