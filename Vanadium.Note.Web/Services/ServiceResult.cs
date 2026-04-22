namespace Vanadium.Note.Web.Services;

public sealed class ServiceResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private ServiceResult(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static ServiceResult<T> Ok(T value) => new(true, value, null);
    public static ServiceResult<T> Fail(string error) => new(false, default, error);
}
