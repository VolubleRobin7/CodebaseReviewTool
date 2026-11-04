using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Processes;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Git;

/// <summary>Runs <c>git &lt;args&gt;</c> in a directory through <see cref="IProcessRunner"/>, using <c>Git:ExecutablePath</c>.</summary>
public sealed class GitRunner(IProcessRunner processRunner, IOptions<GitOptions> options)
{
    /// <summary>Default for short metadata commands. Long-running ones (fetch, worktree add) pass their own.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly IProcessRunner _processRunner = processRunner;
    private readonly GitOptions _options = options.Value;

    public string ExecutablePath => _options.ExecutablePath;

    public Task<ProcessResult> RunAsync(
        string? workingDirectory,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var request = new ProcessRequest(_options.ExecutablePath, [.. arguments], workingDirectory, Timeout: timeout ?? DefaultTimeout);
        return _processRunner.RunAsync(request, cancellationToken);
    }

    public Task<ProcessResult> RunAsync(string? workingDirectory, params string[] arguments) =>
        RunAsync(workingDirectory, arguments, timeout: null, CancellationToken.None);
}
