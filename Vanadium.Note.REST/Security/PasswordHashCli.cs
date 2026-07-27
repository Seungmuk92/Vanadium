namespace Vanadium.Note.REST.Security;

/// <summary>
/// Server-local password-hash generation path (issue #296).
///
/// Invoked with the <c>--hash-password</c> startup flag, it reads a candidate password from
/// standard input, validates it against the very same password policy the API enforces, prints
/// the PBKDF2 storage hash to standard output, and returns an exit code — the web host is never
/// started and the database is never touched.
///
/// This gives an owner who only runs the production instance an official way to rotate
/// <c>Auth:PasswordHash</c>: the <c>/api/auth/hash</c> endpoint deliberately stays
/// Development-only. Requiring shell access to the running server IS the authorization control,
/// and — like the endpoint — nothing is persisted. The password is read from stdin (not from a
/// command-line argument) so it never lands in the shell history or the process argument list.
/// </summary>
public static class PasswordHashCli
{
    /// <summary>Startup flag that switches the process into hash-generation mode.</summary>
    public const string Flag = "--hash-password";

    /// <summary>True when <paramref name="args"/> requests hash-generation mode.</summary>
    public static bool IsInvocation(string[] args) =>
        args.Any(a => string.Equals(a, Flag, StringComparison.Ordinal));

    /// <summary>
    /// Reads a password from <paramref name="input"/>, validates it, and writes its storage hash
    /// to <paramref name="output"/> (only the hash — prompts and errors go to <paramref name="error"/>,
    /// so <c>&gt; hash.txt</c> captures a clean value). Returns <c>0</c> on success, <c>1</c> when no
    /// password was supplied or it fails the security policy.
    /// </summary>
    public static async Task<int> RunAsync(
        IPasswordValidator validator,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        error.WriteLine("Enter the new password (read from stdin), then press Enter:");

        // ReadLine strips the trailing newline; the password is otherwise used verbatim (no trim)
        // so it hashes identically to what /api/auth/login later verifies.
        var password = await input.ReadLineAsync(cancellationToken);

        if (string.IsNullOrEmpty(password))
        {
            error.WriteLine("No password was provided on stdin.");
            return 1;
        }

        var validation = await validator.ValidateAsync(password, cancellationToken);
        if (!validation.IsValid)
        {
            error.WriteLine("Password does not meet the security policy:");
            foreach (var message in validation.Errors)
                error.WriteLine($"  - {message}");
            return 1;
        }

        output.WriteLine(PasswordHasher.Hash(password));
        return 0;
    }
}
