namespace Vanadium.Note.Web.Services;

public sealed class ServiceResult<T>
{
    public bool IsSuccess { get; }
    public bool IsConflict { get; }

    /// <summary>True when the server rejected the request with 403 —
    /// e.g. a mutation against an archived (read-only) note.</summary>
    public bool IsForbidden { get; }

    /// <summary>True when the server responded with 404 — the resource genuinely
    /// does not exist. Distinct from a transient 5xx/network failure, which is
    /// reported as a plain <see cref="Fail"/> so callers do not misjudge an
    /// existing resource as deleted.</summary>
    public bool IsNotFound { get; }

    public T? Value { get; }
    public string? Error { get; }

    private ServiceResult(bool isSuccess, T? value, string? error, bool isConflict = false, bool isForbidden = false, bool isNotFound = false)
    {
        IsSuccess = isSuccess;
        IsConflict = isConflict;
        IsForbidden = isForbidden;
        IsNotFound = isNotFound;
        Value = value;
        Error = error;
    }

    public static ServiceResult<T> Ok(T value) => new(true, value, null);
    public static ServiceResult<T> Fail(string error) => new(false, default, error);
    public static ServiceResult<T> Conflict() => new(false, default, null, isConflict: true);
    public static ServiceResult<T> Forbidden() => new(false, default, null, isForbidden: true);
    public static ServiceResult<T> NotFound(string error) => new(false, default, error, isNotFound: true);
}
