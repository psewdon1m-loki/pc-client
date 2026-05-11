namespace Client.Core;

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "OK") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public sealed record OperationResult<T>(bool Success, T? Value, string Message)
{
    public static OperationResult<T> Ok(T value, string message = "OK") => new(true, value, message);
    public static OperationResult<T> Fail(string message) => new(false, default, message);
}

