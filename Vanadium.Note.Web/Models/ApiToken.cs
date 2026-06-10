namespace Vanadium.Note.Web.Models;

public class CreateApiTokenRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Days until expiry. Null means the token never expires.</summary>
    public int? ExpiresInDays { get; set; }
}

public class CreateApiTokenResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ApiTokenSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenSuffix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
