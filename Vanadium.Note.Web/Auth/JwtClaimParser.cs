using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;

namespace Vanadium.Note.Web.Auth;

/// <summary>
/// Pure, dependency-free parsing of a JWT's payload segment into claims (issue #297).
///
/// <para>
/// A JWT payload is <c>base64url</c>-encoded (RFC 7515 §2: <c>-</c>/<c>_</c> instead of
/// <c>+</c>/<c>/</c>, padding stripped), NOT standard base64. Decoding it with
/// <c>Convert.FromBase64String</c> throws <c>FormatException</c> whenever the encoded
/// <c>exp</c>/<c>iat</c> bytes happen to produce a <c>-</c> or <c>_</c>, which previously left the
/// user authenticated but nameless and made the Settings/Logout buttons vanish intermittently.
/// <see cref="System.Buffers.Text.Base64Url"/> decodes base64url directly, including the missing
/// padding, so any valid token parses.
/// </para>
///
/// <para>
/// This type is deliberately free of Blazor/JS-interop dependencies so it can be unit-tested.
/// </para>
/// </summary>
public static class JwtClaimParser
{
    /// <summary>
    /// Parses the claims out of <paramref name="jwt"/>'s payload segment. Returns an empty list if
    /// the token is malformed, the payload segment is missing, or the payload is not valid JSON.
    /// </summary>
    public static IReadOnlyList<Claim> ParseClaims(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return [];

        var segments = jwt.Split('.');
        if (segments.Length < 2) return [];

        var payload = segments[1];
        if (payload.Length == 0) return [];

        try
        {
            var jsonBytes = Base64Url.DecodeFromChars(payload);
            var pairs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
            if (pairs is null) return [];
            return pairs.Select(p => new Claim(p.Key, p.Value.ToString())).ToList();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Returns true when <paramref name="claims"/> carry an <c>exp</c> claim (Unix seconds) whose
    /// instant is at or before <paramref name="now"/>. A token without a parseable <c>exp</c> is
    /// treated as not-expired, so this never rejects a token on the basis of a missing claim.
    /// </summary>
    public static bool IsExpired(IEnumerable<Claim> claims, DateTimeOffset now)
    {
        var exp = claims.FirstOrDefault(c => c.Type == "exp")?.Value;
        if (exp is null || !long.TryParse(exp, out var seconds)) return false;
        return DateTimeOffset.FromUnixTimeSeconds(seconds) <= now;
    }
}
