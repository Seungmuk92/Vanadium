namespace Vanadium.Note.REST.Security;

/// <summary>
/// Outcome of a password policy check. <see cref="IsValid"/> is true only when
/// <see cref="Errors"/> is empty.
/// </summary>
public sealed class PasswordValidationResult
{
    private PasswordValidationResult(IReadOnlyList<string> errors) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public static PasswordValidationResult Success { get; } = new([]);

    public static PasswordValidationResult Failed(IReadOnlyList<string> errors) => new(errors);
}
