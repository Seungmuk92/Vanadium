using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

/// <summary>Request body for creating a new personal access token.</summary>
public class CreateApiTokenRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Number of days until the token expires. Null means the token never expires.
    /// </summary>
    [Range(1, 3650)]
    public int? ExpiresInDays { get; set; }
}

/// <summary>
/// Response returned once at creation time. The plaintext <see cref="Token"/> is
/// never retrievable again after this response.
/// </summary>
public class CreateApiTokenResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Non-secret view of a token, safe to list in the UI.</summary>
public class ApiTokenSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenSuffix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
