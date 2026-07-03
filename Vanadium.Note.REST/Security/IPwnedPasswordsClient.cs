namespace Vanadium.Note.REST.Security;

/// <summary>
/// Checks a password against the Have I Been Pwned breach corpus.
/// </summary>
public interface IPwnedPasswordsClient
{
    /// <summary>
    /// Returns how many times the password appears in known breaches, or 0 if it is
    /// not found. Returns 0 (fails open) when the service cannot be reached.
    /// </summary>
    Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken = default);
}
