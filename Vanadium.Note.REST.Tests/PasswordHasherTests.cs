using System.Security.Cryptography;
using System.Text;
using Vanadium.Note.REST.Security;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// PBKDF2 password hashing (issue #111): the storage format encodes the iteration count
/// (<c>salt:hash:iterations</c>) so it can be raised without rehashing, verification uses the
/// stored count, and legacy two-part hashes remain accepted at 100k iterations.
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesThreePartFormat_WithDefaultIterations()
    {
        var stored = PasswordHasher.Hash("correct horse battery staple");

        var parts = stored.Split(':');
        Assert.Equal(3, parts.Length);
        Assert.Equal(PasswordHasher.DefaultIterations, int.Parse(parts[2]));
        // salt and hash are valid base64.
        Assert.NotEmpty(Convert.FromBase64String(parts[0]));
        Assert.NotEmpty(Convert.FromBase64String(parts[1]));
    }

    [Fact]
    public void DefaultIterations_MeetsOwaspGuidance()
    {
        Assert.True(PasswordHasher.DefaultIterations >= 600_000);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        // Random salt per hash: identical passwords must not collide.
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var stored = PasswordHasher.Hash("s3cret-pass");
        Assert.True(PasswordHasher.Verify("s3cret-pass", stored));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var stored = PasswordHasher.Hash("s3cret-pass");
        Assert.False(PasswordHasher.Verify("wrong-pass", stored));
    }

    [Fact]
    public void Hash_WithExplicitIterations_EncodesThatCount()
    {
        var stored = PasswordHasher.Hash("pw", 250_000);

        Assert.Equal("250000", stored.Split(':')[2]);
        Assert.True(PasswordHasher.Verify("pw", stored));
    }

    [Fact]
    public void Verify_UsesStoredIterationCount_NotTheDefault()
    {
        // A hash produced at a non-default iteration count must still verify — proving the
        // count is read from the stored value rather than assumed to be DefaultIterations.
        var stored = PasswordHasher.Hash("migrate-me", 100_000);

        Assert.NotEqual(PasswordHasher.DefaultIterations, int.Parse(stored.Split(':')[2]));
        Assert.True(PasswordHasher.Verify("migrate-me", stored));
    }

    [Fact]
    public void Verify_LegacyTwoPartHash_VerifiesAt100k()
    {
        // Reproduce the pre-#111 format (salt:hash, implicitly 100k iterations) exactly.
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("legacy-pw"),
            salt,
            PasswordHasher.LegacyIterations,
            HashAlgorithmName.SHA256,
            32);
        var legacyStored = $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";

        Assert.True(PasswordHasher.Verify("legacy-pw", legacyStored));
        Assert.False(PasswordHasher.Verify("not-the-pw", legacyStored));
    }

    [Theory]
    [InlineData("")]                                 // empty
    [InlineData("onlyonepart")]                      // 1 part
    [InlineData("a:b:c:d")]                          // 4 parts
    [InlineData("c2FsdA==:aGFzaA==:notanumber")]     // non-numeric iterations
    [InlineData("c2FsdA==:aGFzaA==:0")]              // zero iterations
    [InlineData("c2FsdA==:aGFzaA==:-5")]             // negative iterations
    [InlineData("not_base64:aGFzaA==:600000")]       // invalid base64 salt
    public void Verify_MalformedHash_ReturnsFalse(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("anything", storedHash));
    }

    [Fact]
    public void Hash_NonPositiveIterations_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("pw", 0));
    }
}
