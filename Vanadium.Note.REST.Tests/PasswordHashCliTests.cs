using Vanadium.Note.REST.Security;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// The server-local <c>--hash-password</c> path (issue #296): it must produce a hash that
/// <see cref="PasswordHasher.Verify"/> accepts, reject weak passwords using the same policy as
/// the API, and never emit anything but the hash on stdout.
/// </summary>
public class PasswordHashCliTests
{
    private sealed class StubValidator(PasswordValidationResult result) : IPasswordValidator
    {
        public string? LastPassword { get; private set; }

        public Task<PasswordValidationResult> ValidateAsync(
            string password, CancellationToken cancellationToken = default)
        {
            LastPassword = password;
            return Task.FromResult(result);
        }
    }

    [Theory]
    [InlineData(new[] { "--hash-password" }, true)]
    [InlineData(new[] { "run", "--hash-password", "extra" }, true)]
    [InlineData(new[] { "--Hash-Password" }, false)] // case-sensitive: not the flag
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--urls", "http://+:8080" }, false)]
    public void IsInvocation_DetectsFlagExactly(string[] args, bool expected)
    {
        Assert.Equal(expected, PasswordHashCli.IsInvocation(args));
    }

    [Fact]
    public async Task RunAsync_ValidPassword_PrintsVerifiableHashAndReturnsZero()
    {
        const string password = "correct horse battery staple";
        var validator = new StubValidator(PasswordValidationResult.Success);
        using var input = new StringReader(password + "\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await PasswordHashCli.RunAsync(validator, input, output, error);

        Assert.Equal(0, exitCode);
        var hash = output.ToString().Trim();
        // stdout carries ONLY the hash, nothing else — so `> hash.txt` captures a clean value.
        Assert.Equal(hash, output.ToString().TrimEnd('\r', '\n'));
        Assert.True(PasswordHasher.Verify(password, hash));
        Assert.Equal(password, validator.LastPassword); // validated verbatim, no trimming
    }

    [Fact]
    public async Task RunAsync_WeakPassword_PrintsErrorsAndReturnsOneWithNoHash()
    {
        var validator = new StubValidator(
            PasswordValidationResult.Failed(["Password must be at least 15 characters long."]));
        using var input = new StringReader("short\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await PasswordHashCli.RunAsync(validator, input, output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString()); // no hash leaked to stdout on rejection
        Assert.Contains("at least 15 characters", error.ToString());
    }

    [Fact]
    public async Task RunAsync_EmptyStdin_ReturnsOneWithoutCallingValidator()
    {
        var validator = new StubValidator(PasswordValidationResult.Success);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await PasswordHashCli.RunAsync(validator, input, output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Null(validator.LastPassword); // short-circuited before validation
    }
}
