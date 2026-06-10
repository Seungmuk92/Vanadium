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
    /// Creates a token for the user and returns the one-time plaintext value alongside
    /// the persisted record. The plaintext is never stored and cannot be recovered later.
    /// </summary>
    public async Task<(ApiToken Token, string Plaintext)> CreateAsync(
        Guid userId, string name, int? expiresInDays, CancellationToken ct = default)
    {
        var randomPart = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        var plaintext = TokenPrefix + randomPart;

        var token = new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            TokenHash = HashToken(plaintext),
            TokenSuffix = plaintext[^4..],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresInDays is { } days ? DateTime.UtcNow.AddDays(days) : null
        };

        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("API token {TokenId} '{TokenName}' created for user {UserId}",
            token.Id, token.Name, userId);

        return (token, plaintext);
    }

    public async Task<List<ApiToken>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.ApiTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>Deletes a token owned by the user. Returns false if it does not exist.</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid tokenId, CancellationToken ct = default)
    {
        var token = await db.ApiTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, ct);
        if (token is null)
            return false;

        db.ApiTokens.Remove(token);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("API token {TokenId} revoked by user {UserId}", tokenId, userId);
        return true;
    }
}
