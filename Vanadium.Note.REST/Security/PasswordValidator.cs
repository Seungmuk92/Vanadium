using Microsoft.Extensions.Options;

namespace Vanadium.Note.REST.Security;

/// <summary>
/// Enforces the Vanadium password policy: length over composition, no context words,
/// no obvious keyboard/sequence/repeat patterns, not a well-known common password,
/// and not present in the Have I Been Pwned breach corpus.
/// </summary>
public sealed class PasswordValidator(
    IOptions<PasswordPolicyOptions> options,
    IPwnedPasswordsClient breachClient) : IPasswordValidator
{
    private readonly PasswordPolicyOptions _options = options.Value;

    /// <summary>Keyboard runs scanned in both directions for substrings of length 4+.</summary>
    private static readonly string[] KeyboardRows =
    [
        "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm"
    ];

    /// <summary>A small local blocklist for offline coverage; the breach check handles the long tail.</summary>
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "123456", "12345678", "123456789", "1234567890",
        "qwerty", "qwerty123", "111111", "000000", "iloveyou", "admin", "welcome",
        "monkey", "dragon", "letmein", "abc123", "passw0rd", "p@ssw0rd", "changeme",
        "secret", "master", "sunshine", "princess", "football", "baseball"
    };

    public async Task<PasswordValidationResult> ValidateAsync(
        string password, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Password must not be empty.");
            return PasswordValidationResult.Failed(errors);
        }

        if (password.Length < _options.MinLength)
            errors.Add($"Password must be at least {_options.MinLength} characters long.");

        foreach (var term in _options.ContextTerms)
        {
            if (!string.IsNullOrEmpty(term)
                && password.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Password must not contain the guessable term \"{term}\".");
            }
        }

        if (HasRepeatedCharacterRun(password))
            errors.Add("Password must not contain a character repeated 4 or more times in a row.");

        if (HasSequentialRun(password))
            errors.Add("Password must not contain sequential runs such as \"1234\" or \"abcd\".");

        if (ContainsKeyboardPattern(password))
            errors.Add("Password must not contain keyboard patterns such as \"qwerty\".");

        if (CommonPasswords.Contains(password.Trim()))
            errors.Add("Password is a well-known common password.");

        if (_options.EnableBreachCheck)
        {
            var breachCount = await breachClient.GetBreachCountAsync(password, cancellationToken);
            if (breachCount > 0)
                errors.Add($"Password has appeared in known data breaches ({breachCount:N0} times) — choose a different one.");
        }

        return errors.Count == 0
            ? PasswordValidationResult.Success
            : PasswordValidationResult.Failed(errors);
    }

    private static bool HasRepeatedCharacterRun(string password)
    {
        var run = 1;
        for (var i = 1; i < password.Length; i++)
        {
            run = password[i] == password[i - 1] ? run + 1 : 1;
            if (run >= 4)
                return true;
        }
        return false;
    }

    private static bool HasSequentialRun(string password)
    {
        var ascending = 1;
        var descending = 1;
        for (var i = 1; i < password.Length; i++)
        {
            var delta = password[i] - password[i - 1];
            ascending = delta == 1 ? ascending + 1 : 1;
            descending = delta == -1 ? descending + 1 : 1;
            if (ascending >= 4 || descending >= 4)
                return true;
        }
        return false;
    }

    private static bool ContainsKeyboardPattern(string password)
    {
        var lower = password.ToLowerInvariant();
        foreach (var row in KeyboardRows)
        {
            for (var start = 0; start + 4 <= row.Length; start++)
            {
                var forward = row.Substring(start, 4);
                var backward = new string(forward.Reverse().ToArray());
                if (lower.Contains(forward) || lower.Contains(backward))
                    return true;
            }
        }
        return false;
    }
}
