using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;
using Vanadium.Note.Web.Auth;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression coverage for issue #297: the client parses a JWT payload as <c>base64url</c>, so a
/// token whose payload contains <c>-</c>/<c>_</c> must still yield its claims (the old
/// <c>Convert.FromBase64String</c> path threw and produced an authenticated-but-nameless user,
/// which made the Settings/Logout buttons vanish). Also pins the new <c>exp</c> expiry check.
/// </summary>
public class JwtClaimParserTests
{
    // Builds a real 3-segment JWT (header.payload.signature) with the given payload object,
    // payload encoded as base64url exactly like a server-issued token.
    private static string BuildJwt(object payload)
    {
        static string Segment(ReadOnlySpan<byte> bytes) => Base64Url.EncodeToString(bytes);
        var header = Segment("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8);
        var body = Segment(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.{Segment("signature"u8)}";
    }

    private static long Unix(DateTimeOffset when) => when.ToUnixTimeSeconds();

    // A base64url payload contains '-' (index 62) / '_' (index 63) only where a specific byte lands
    // on a 3-byte-group boundary: '~'(0x7E)&63 = 62 -> '-', '>'(0x3E) = 62 -> '-', '?'(0x3F) = 63 ->
    // '_'. A run of three identical such chars covers all three offsets mod 3, so one is guaranteed
    // to hit the boundary regardless of the marker's position — giving a deterministic base64url
    // payload that the old Convert.FromBase64String path rejected.
    [Theory]
    [InlineData('-', "~~~")]
    [InlineData('_', "???")]
    public void ParseClaims_PayloadWithBase64UrlChar_ParsesUsername(char urlChar, string marker)
    {
        var jwt = BuildJwt(new { unique_name = "owner", marker });
        var body = jwt.Split('.')[1];
        Assert.Contains(urlChar, body); // sanity: this token genuinely exercises base64url

        var claims = JwtClaimParser.ParseClaims(jwt);

        Assert.Equal("owner", claims.FirstOrDefault(c => c.Type == "unique_name")?.Value);
    }

    [Theory]
    [InlineData("~~~")]
    [InlineData("???")]
    public void ParseClaims_PayloadStandardBase64WouldReject_StillDecodes(string marker)
    {
        // Prove the regression: the old path (Convert.FromBase64String after the previous code's
        // %4 padding) throws on exactly these base64url payloads, yet the new parser still decodes.
        var jwt = BuildJwt(new { unique_name = "owner", marker });
        var body = jwt.Split('.')[1];
        var padded = (body.Length % 4) switch { 2 => body + "==", 3 => body + "=", _ => body };
        Assert.Throws<FormatException>(() => Convert.FromBase64String(padded));

        var claims = JwtClaimParser.ParseClaims(jwt);

        Assert.Equal("owner", claims.FirstOrDefault(c => c.Type == "unique_name")?.Value);
    }

    [Fact]
    public void ParseClaims_ValidToken_ReturnsAllClaims()
    {
        var jwt = BuildJwt(new { unique_name = "owner", exp = Unix(DateTimeOffset.UtcNow.AddHours(1)) });

        var claims = JwtClaimParser.ParseClaims(jwt);

        Assert.Contains(claims, c => c.Type == "unique_name" && c.Value == "owner");
        Assert.Contains(claims, c => c.Type == "exp");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]        // no dot, single segment
    [InlineData("header.only")]      // payload is "only" — invalid base64url/JSON
    [InlineData("a..c")]             // empty payload segment
    public void ParseClaims_MalformedToken_ReturnsEmpty(string? jwt)
    {
        Assert.Empty(JwtClaimParser.ParseClaims(jwt));
    }

    [Fact]
    public void IsExpired_ExpInPast_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new[] { new Claim("exp", Unix(now.AddMinutes(-1)).ToString()) };

        Assert.True(JwtClaimParser.IsExpired(claims, now));
    }

    [Fact]
    public void IsExpired_ExpInFuture_ReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new[] { new Claim("exp", Unix(now.AddMinutes(1)).ToString()) };

        Assert.False(JwtClaimParser.IsExpired(claims, now));
    }

    [Fact]
    public void IsExpired_NoExpClaim_ReturnsFalse()
    {
        var claims = new[] { new Claim("unique_name", "owner") };

        Assert.False(JwtClaimParser.IsExpired(claims, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_UnparseableExp_ReturnsFalse()
    {
        var claims = new[] { new Claim("exp", "not-a-number") };

        Assert.False(JwtClaimParser.IsExpired(claims, DateTimeOffset.UtcNow));
    }
}
