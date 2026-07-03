namespace Vanadium.Note.REST.Security;

/// <summary>
/// Configuration for <see cref="PasswordValidator"/>. Bound from the
/// <c>Auth:PasswordPolicy</c> configuration section.
/// </summary>
public sealed class PasswordPolicyOptions
{
    public const string SectionName = "Auth:PasswordPolicy";

    /// <summary>Minimum accepted password length. Length is favored over composition rules.</summary>
    public int MinLength { get; set; } = 15;

    /// <summary>
    /// Context words that must not appear (case-insensitively) anywhere in the password —
    /// app name, owner label, and other guessable terms tied to this deployment.
    /// </summary>
    public string[] ContextTerms { get; set; } =
    [
        "vanadium", "note", "owner", "admin", "root",
        "password", "passwd", "login", "secret", "qwerty"
    ];

    /// <summary>
    /// When true, candidate passwords are checked against the Have I Been Pwned
    /// breach corpus using the k-anonymity range API. The check fails open: if the
    /// service is unreachable, validation proceeds without it (a warning is logged).
    /// </summary>
    public bool EnableBreachCheck { get; set; } = true;
}
