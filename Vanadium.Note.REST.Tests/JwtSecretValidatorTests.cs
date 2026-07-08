using System.Text;
using Vanadium.Note.REST.Auth;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Startup fail-fast validation for <c>Auth:JwtSecret</c> (issue #110): a missing, empty,
/// or shorter-than-32-byte secret must throw a clear exception; a valid secret passes through.
/// </summary>
public class JwtSecretValidatorTests
{
    [Fact]
    public void Validate_NullSecret_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(null));
        Assert.Contains("Auth:JwtSecret", ex.Message);
    }

    [Fact]
    public void Validate_EmptySecret_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(string.Empty));
        Assert.Contains("Auth:JwtSecret", ex.Message);
    }

    [Fact]
    public void Validate_TooShortSecret_ThrowsWithByteLength()
    {
        // 31 ASCII bytes — one short of the 32-byte HS256 minimum.
        var secret = new string('a', JwtSecretValidator.MinimumByteLength - 1);

        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(secret));
        Assert.Contains("32 bytes", ex.Message);
        Assert.Contains("31 byte", ex.Message);
    }

    [Fact]
    public void Validate_MultiByteCharsCountedByBytes_Throws()
    {
        // 'é' is 2 bytes in UTF-8: 15 chars = 30 bytes < 32, so the check must count
        // bytes (not chars) to reject this even though it is 30 characters long.
        var secret = new string('é', 15);

        Assert.Equal(30, Encoding.UTF8.GetByteCount(secret));
        Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(secret));
    }

    [Fact]
    public void Validate_ExactMinimumLength_ReturnsSecret()
    {
        var secret = new string('a', JwtSecretValidator.MinimumByteLength); // exactly 32 bytes

        Assert.Equal(secret, JwtSecretValidator.Validate(secret));
    }

    [Fact]
    public void Validate_LongerSecret_ReturnsSecret()
    {
        var secret = new string('a', 64);

        Assert.Equal(secret, JwtSecretValidator.Validate(secret));
    }
}
