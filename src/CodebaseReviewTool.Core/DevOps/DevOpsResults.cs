using System.Net;

namespace CodebaseReviewTool.Core.DevOps;

/// <summary>Outcome of a DevOps call that returns one item.</summary>
/// <param name="Succeeded">True when the call returned the item.</param>
/// <param name="Value">The item on success.</param>
/// <param name="Error">What went wrong on failure, including the HTTP status and request path when there was one.</param>
public sealed record DevOpsResult<T>(bool Succeeded, T? Value, string? Error)
    where T : class
{
    /// <summary>
    /// The HTTP status of the response to a read (GET) call, so a caller can tell, say, a 404 (no such item) from
    /// other failures; null when no response arrived (network failure or timeout) and on results of write calls,
    /// which return their status separately.
    /// </summary>
    public HttpStatusCode? StatusCode { get; init; }

    public static DevOpsResult<T> Success(T value) => new(true, value, null);

    public static DevOpsResult<T> Failure(string error) => new(false, null, error);
}

/// <summary>Outcome of a DevOps call that returns a list.</summary>
/// <param name="Succeeded">True when every page of the list was read.</param>
/// <param name="Items">The items on success; empty on failure.</param>
/// <param name="Error">What went wrong on failure, including the HTTP status and request path when there was one.</param>
public sealed record DevOpsListResult<T>(bool Succeeded, IReadOnlyList<T> Items, string? Error)
{
    public static DevOpsListResult<T> Success(IReadOnlyList<T> items) => new(true, items, null);

    public static DevOpsListResult<T> Failure(string error) => new(false, [], error);
}
