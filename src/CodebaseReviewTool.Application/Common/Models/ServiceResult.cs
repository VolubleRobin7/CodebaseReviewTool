namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>
/// Outcome of a call to an outside service (git, the repository host) whose expected failures are returned, not thrown.
/// </summary>
/// <param name="Succeeded">True when the call returned its value.</param>
/// <param name="Value">The value on success.</param>
/// <param name="Error">What went wrong on failure, written for the user.</param>
public sealed record ServiceResult<T>(bool Succeeded, T? Value, string? Error)
    where T : class
{
    /// <summary>True when the call failed because the thing asked for does not exist (an HTTP 404, say).</summary>
    public bool IsNotFound { get; init; }

    public static ServiceResult<T> Success(T value) => new(true, value, null);

    public static ServiceResult<T> Failure(string error) => new(false, null, error);

    public static ServiceResult<T> NotFound(string error) => new(false, null, error) { IsNotFound = true };
}
