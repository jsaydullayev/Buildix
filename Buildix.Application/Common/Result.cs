namespace Buildix.Application.Common;

/// <summary>
/// Lightweight Result / Notification type — the clean-architecture alternative
/// to throwing exceptions for *expected* business failures (not-found, validation,
/// conflicts). Services return a <see cref="Result"/> / <see cref="Result{T}"/>;
/// controllers translate it to an HTTP response. Exceptions stay reserved for
/// genuinely exceptional/unexpected faults.
///
/// This is the Buildix-native equivalent of the reference project's
/// <c>StatusGenericHandler</c> — dependency-free, so no extra NuGet package.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary>Human-readable error message (null on success).</summary>
    public string? Error { get; }

    /// <summary>Optional machine-readable code (e.g. "NOT_FOUND", "USERNAME_TAKEN").</summary>
    public string? Code { get; }

    protected Result(bool isSuccess, string? error, string? code)
    {
        IsSuccess = isSuccess;
        Error = error;
        Code = code;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, string? code = null) => new(false, error, code);

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Failure<T>(string error, string? code = null) => Result<T>.Fail(error, code);
}

/// <summary>Result that carries a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    /// <summary>The success value. Throws if accessed on a failed result.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed Result.");

    /// <summary>Non-throwing accessor — returns default(T) on failure.</summary>
    public T? ValueOrDefault => _value;

    private Result(bool isSuccess, T? value, string? error, string? code)
        : base(isSuccess, error, code) => _value = value;

    public static Result<T> Ok(T value) => new(true, value, null, null);
    public static Result<T> Fail(string error, string? code = null) => new(false, default, error, code);
}
