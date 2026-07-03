using System.Security.Cryptography;
using System.Text;

namespace Vanadium.Note.REST.Security;

/// <summary>
/// Have I Been Pwned "Pwned Passwords" range client. Uses the k-anonymity model:
/// only the first 5 characters of the password's SHA-1 hash are sent over the wire,
/// and the matching suffix is compared locally — the plaintext never leaves the server.
/// </summary>
public sealed class PwnedPasswordsClient(HttpClient httpClient, ILogger<PwnedPasswordsClient> logger)
    : IPwnedPasswordsClient
{
    public async Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        var hash = Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[..5];
        var suffix = hash[5..];

        try
        {
            using var response = await httpClient.GetAsync(
                $"range/{prefix}", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                // Each line is "<HASH_SUFFIX>:<COUNT>".
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                if (suffix.Equals(line[..separator], StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line[(separator + 1)..], out var count))
                {
                    return count;
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Fail open: an unreachable breach service must not block hashing. The
            // remaining structural policy rules still apply.
            logger.LogWarning(ex,
                "Pwned Passwords breach check unavailable — skipping breach validation for this request.");
            return 0;
        }
    }
}
