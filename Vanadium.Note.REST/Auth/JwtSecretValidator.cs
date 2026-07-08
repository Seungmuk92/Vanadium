using System.Text;

namespace Vanadium.Note.REST.Auth;

/// <summary>
/// Validates the configured <c>Auth:JwtSecret</c> at application startup so a missing,
/// empty, or too-short secret fails fast with a clear message instead of surfacing as an
/// opaque error later. HS256 signing requires a key of at least 256 bits (32 bytes); a
/// shorter secret weakens the signature and is rejected.
/// </summary>
public static class JwtSecretValidator
{
    /// <summary>Minimum secret length in bytes required for HS256 (a 256-bit key).</summary>
    public const int MinimumByteLength = 32;

    /// <summary>
    /// Ensures <paramref name="secret"/> is present and at least <see cref="MinimumByteLength"/>
    /// bytes long. Returns the validated secret unchanged so callers can assign it inline.
    /// </summary>
    /// <param name="secret">The raw <c>Auth:JwtSecret</c> value read from configuration.</param>
    /// <returns>The validated secret.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the secret is null, empty, or shorter than <see cref="MinimumByteLength"/> bytes.
    /// </exception>
    public static string Validate(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                "Auth:JwtSecret is not configured. Set Auth:JwtSecret in appsettings.");
        }

        var byteLength = Encoding.UTF8.GetByteCount(secret);
        if (byteLength < MinimumByteLength)
        {
            throw new InvalidOperationException(
                $"Auth:JwtSecret must be at least {MinimumByteLength} bytes for HS256 signing, " +
                $"but the configured value is {byteLength} byte(s). Configure a longer secret in appsettings.");
        }

        return secret;
    }
}
