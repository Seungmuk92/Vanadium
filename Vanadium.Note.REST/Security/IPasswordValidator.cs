namespace Vanadium.Note.REST.Security;

/// <summary>
/// Validates a candidate password against the configured password policy.
/// </summary>
public interface IPasswordValidator
{
    Task<PasswordValidationResult> ValidateAsync(
        string password, CancellationToken cancellationToken = default);
}
