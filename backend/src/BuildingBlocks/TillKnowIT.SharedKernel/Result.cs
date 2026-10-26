namespace TillKnowIT.SharedKernel;

public sealed record Error(string Code, string Message);

public class Result
{
    protected Result(bool ok, Error? error) { IsSuccess = ok; Error = error; }
    public bool IsSuccess { get; }
    public Error? Error { get; }
    public static Result Ok() => new(true, null);
    public static Result Fail(string code, string message) => new(false, new Error(code, message));
}

public sealed class Result<T> : Result
{
    private Result(T? value, bool ok, Error? error) : base(ok, error) => Value = value;
    public T? Value { get; }
    public static Result<T> Ok(T value) => new(value, true, null);
    public static new Result<T> Fail(string code, string message) => new(default, false, new Error(code, message));
}
