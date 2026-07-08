using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vanadium.Note.REST.Security;

/// <summary>
/// PBKDF2-SHA256 hashing for the single owner password (<c>Auth:PasswordHash</c>).
///
/// The storage format encodes the iteration count so it can be raised in future without
/// rehashing already-configured values: verification always uses the iteration count read
/// from the stored hash, while newly produced hashes use <see cref="DefaultIterations"/>.
///
/// Storage format: <c>base64(salt):base64(hash):iterations</c>. Legacy two-part values
/// (<c>base64(salt):base64(hash)</c>) produced before iteration encoding are still accepted
/// and verified at <see cref="LegacyIterations"/>, so an existing <c>Auth:PasswordHash</c>
/// keeps working with zero downtime until it is regenerated.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Iteration count for newly produced hashes (OWASP PBKDF2-SHA256 guidance).</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Iteration count assumed for legacy two-part hashes that predate iteration encoding.</summary>
    public const int LegacyIterations = 100_000;

    private const int SaltByteLength = 16;
    private const int HashByteLength = 32;

    /// <summary>Hashes <paramref name="password"/> with <see cref="DefaultIterations"/>.</summary>
    public static string Hash(string password) => Hash(password, DefaultIterations);

    /// <summary>
    /// Hashes <paramref name="password"/> with an explicit <paramref name="iterations"/> count,
    /// encoding that count into the returned storage string.
    /// </summary>
    public static string Hash(string password, int iterations)
    {
        if (iterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count must be positive.");

        var salt = RandomNumberGenerator.GetBytes(SaltByteLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashByteLength);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}:{iterations}";
    }

    /// <summary>
    /// Verifies <paramref name="password"/> against <paramref name="storedHash"/>, using the
    /// iteration count encoded in the hash (three-part) or <see cref="LegacyIterations"/> for a
    /// legacy two-part hash. Returns <c>false</c> for any malformed or unparseable value rather
    /// than throwing.
    /// </summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;

        var parts = storedHash.Split(':');
        int iterations;
        switch (parts.Length)
        {
            case 2:
                iterations = LegacyIterations;
                break;
            case 3:
                if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                    || iterations <= 0)
                    return false;
                break;
            default:
                return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }
}
