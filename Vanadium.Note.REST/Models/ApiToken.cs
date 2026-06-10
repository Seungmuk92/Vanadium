using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

/// <summary>
/// A long-lived personal access token (PAT) used to authenticate API requests
/// without going through the interactive login flow. The plaintext value is shown
/// to the user exactly once at creation time; only its SHA-256 hash is persisted.
/// </summary>
public class ApiToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>User-supplied label, e.g. "CI deploy".</summary>
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Base64-encoded SHA-256 hash of the plaintext token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Last 4 characters of the plaintext token, for UI identification.</summary>
    [MaxLength(4)]
    public string TokenSuffix { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Absolute expiry instant. Null means the token never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }
}
