using CodebaseReviewTool.Application.Common.Exceptions;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using CodebaseReviewTool.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Git;

/// <summary>
/// <see cref="ISourceControl"/> on git. Reads the configured clones' remotes, and creates, inspects and removes the
/// worktree each review runs in. Worktrees live under <c>Git:WorktreeRoot</c> as
/// <c>&lt;repositoryKey&gt;/pr-&lt;pullRequestId&gt;-r&lt;reviewId&gt;</c>. Every review git command runs with
/// <c>Git:CommandTimeoutMinutes</c> and is logged with its elapsed time.
/// </summary>
public sealed class GitSourceControl(
    GitRunner gitRunner,
    IOptions<GitOptions> options,
    IOptions<RepositoriesOptions> repositories,
    ILogger<GitSourceControl> logger) : ISourceControl
{
    private const string _headsPrefix = "refs/heads/";

    private readonly GitRunner _gitRunner = gitRunner;
    private readonly GitOptions _options = options.Value;
    private readonly ILogger<GitSourceControl> _logger = logger;

    private readonly IReadOnlyList<ConfiguredClone> _clones = [.. repositories.Value.Items.Select((item, index) => new ConfiguredClone(
        $"{RepositoriesOptions.SectionName}:{index}",
        item.DisplayName,
        item.LocalPath,
        NullIfBlank(item.Remote),
        NullIfBlank(item.Project),
        NullIfBlank(item.Repository)))];

    private TimeSpan CommandTimeout => TimeSpan.FromMinutes(_options.CommandTimeoutMinutes);

    IReadOnlyList<ConfiguredClone> ISourceControl.ConfiguredClones => _clones;

    async Task<ServiceResult<CloneRemote>> ISourceControl.ReadRemoteAsync(ConfiguredClone clone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clone);
        var prefix = clone.Description;

        var remotes = await _gitRunner.RunAsync(clone.LocalPath, ["remote"], cancellationToken: cancellationToken);
        if (!remotes.Succeeded)
        {
            return ServiceResult<CloneRemote>.Failure($"{prefix}: '{clone.LocalPath}' is not a git repository ({FirstLine(remotes.StandardError)}).");
        }

        var remoteNames = remotes.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string remoteName;
        if (clone.RemoteName is { } configured)
        {
            if (!remoteNames.Contains(configured, StringComparer.Ordinal))
            {
                return ServiceResult<CloneRemote>.Failure(
                    $"{prefix}: Remote '{configured}' does not exist in '{clone.LocalPath}' (remotes: {Describe(remoteNames)}).");
            }

            remoteName = configured;
        }
        else if (remoteNames.Contains("origin", StringComparer.Ordinal))
        {
            remoteName = "origin";
        }
        else if (remoteNames.Length == 1)
        {
            remoteName = remoteNames[0];
        }
        else
        {
            return ServiceResult<CloneRemote>.Failure(
                $"{prefix}: cannot choose a remote in '{clone.LocalPath}' (remotes: {Describe(remoteNames)}); set {clone.ConfigurationKey}:Remote.");
        }

        var url = await _gitRunner.RunAsync(clone.LocalPath, ["remote", "get-url", remoteName], cancellationToken: cancellationToken);
        if (!url.Succeeded)
        {
            return ServiceResult<CloneRemote>.Failure($"{prefix}: cannot read the URL of remote '{remoteName}' ({FirstLine(url.StandardError)}).");
        }

        // Redacted so a token embedded in an https remote never reaches the UI or a startup message.
        return ServiceResult<CloneRemote>.Success(new CloneRemote(clone, remoteName, RemoveCredentials(url.StandardOutput.Trim())));
    }

    string ISourceControl.GetWorktreePath(string repositoryKey, int pullRequestId, int reviewId) =>
        GetWorktreePath(repositoryKey, pullRequestId, reviewId);

    async Task<WorktreeInfo> ISourceControl.CreateWorktreeAsync(
        ResolvedRepository repository,
        int pullRequestId,
        int reviewId,
        string sourceCommit,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommit);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);

        if (_options.FetchBeforeReview)
        {
            var fetch = await RunAsync(repository.Key, repository.LocalPath, ["fetch", repository.RemoteName], cancellationToken);
            if (!fetch.Succeeded)
            {
                throw new WorktreeException($"git fetch {repository.RemoteName} failed: {Describe(fetch)}");
            }
        }

        var exists = await RunAsync(repository.Key, repository.LocalPath, ["cat-file", "-e", $"{sourceCommit}^{{commit}}"], cancellationToken);
        if (!exists.Succeeded)
        {
            throw new WorktreeException(_options.FetchBeforeReview
                ? $"The source commit {sourceCommit} is not in the clone at '{repository.LocalPath}' after fetching {repository.RemoteName}."
                : $"The source commit {sourceCommit} is not in the clone at '{repository.LocalPath}' (Git:FetchBeforeReview is off).");
        }

        var path = GetWorktreePath(repository.Key, pullRequestId, reviewId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (Directory.Exists(path))
        {
            // Left behind by a run that stopped part-way; clear it so the add below can reuse the name.
            _logger.LogWarning("Worktree path {Path} already exists; removing it before adding the worktree", path);
            await RemoveWorktreeAsync(repository, path, cancellationToken);
        }

        string[] add = ["worktree", "add", "--detach", path, sourceCommit];
        var added = await RunAsync(repository.Key, repository.LocalPath, add, cancellationToken);
        if (!added.Succeeded)
        {
            // A stale registration (directory deleted by hand, worktree still registered) also blocks the add.
            await RemoveWorktreeAsync(repository, path, cancellationToken);
            added = await RunAsync(repository.Key, repository.LocalPath, add, cancellationToken);
            if (!added.Succeeded)
            {
                throw new WorktreeException($"git worktree add failed for '{path}': {Describe(added)}");
            }
        }

        try
        {
            var branch = targetBranch.StartsWith(_headsPrefix, StringComparison.Ordinal) ? targetBranch[_headsPrefix.Length..] : targetBranch;
            var trackingBranch = $"{repository.RemoteName}/{branch}";

            var tracking = await RunAsync(
                repository.Key, repository.LocalPath, ["rev-parse", "--verify", "--quiet", $"refs/remotes/{trackingBranch}^{{commit}}"], cancellationToken);
            if (!tracking.Succeeded)
            {
                throw new WorktreeException(
                    $"The remote-tracking branch {trackingBranch} (the pull request's target) does not exist in '{repository.LocalPath}'.");
            }

            var mergeBase = await RunAsync(repository.Key, repository.LocalPath, ["merge-base", trackingBranch, sourceCommit], cancellationToken);
            if (!mergeBase.Succeeded)
            {
                throw new WorktreeException($"git merge-base {trackingBranch} {sourceCommit} failed: {Describe(mergeBase)}");
            }

            return new WorktreeInfo(path, mergeBase.StandardOutput.Trim());
        }
        catch (Exception)
        {
            // The caller only records a worktree that is complete, so one left here would never be cleaned up.
            await RemoveWorktreeAsync(repository, path, CancellationToken.None);
            throw;
        }
    }

    async Task<IReadOnlyList<ChangedFile>> ISourceControl.GetChangedFilesAsync(
        string worktreePath,
        string mergeBase,
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergeBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommit);

        // -z keeps paths verbatim (no quoting of unusual characters) and separates every field with a NUL.
        var context = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(worktreePath))) ?? worktreePath;
        var result = await RunAsync(context, worktreePath, ["diff", "--name-status", "-z", mergeBase, sourceCommit], cancellationToken);
        if (!result.Succeeded)
        {
            throw new WorktreeException($"git diff --name-status {mergeBase} {sourceCommit} failed: {Describe(result)}");
        }

        return ParseNameStatus(result.StandardOutput);
    }

    Task<WorktreeRemoveResult> ISourceControl.RemoveWorktreeAsync(ResolvedRepository repository, string worktreePath, CancellationToken cancellationToken) =>
        RemoveWorktreeAsync(repository, worktreePath, cancellationToken);

    /// <summary>
    /// Removes a password or token embedded in an http(s) URL (<c>https://user:secret@host/...</c> becomes
    /// <c>https://user@host/...</c>) so the URL is safe to display or log.
    /// </summary>
    private static string RemoveCredentials(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.UserInfo.Contains(':'))
        {
            return url;
        }

        var builder = new UriBuilder(uri) { Password = string.Empty };
        return builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    internal static IReadOnlyList<ChangedFile> ParseNameStatus(string output)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<ChangedFile>();

        for (var i = 0; i < fields.Length; i++)
        {
            var status = fields[i].Trim();
            if (status.Length == 0)
            {
                continue;
            }

            // Renames and copies carry two paths (old, new); every other status carries one.
            var pathCount = status[0] is 'R' or 'C' ? 2 : 1;
            if (i + pathCount >= fields.Length)
            {
                break;
            }

            files.Add(new ChangedFile(status, fields[i + pathCount]));
            i += pathCount;
        }

        return files;
    }

    private string GetWorktreePath(string repositoryKey, int pullRequestId, int reviewId) =>
        Path.GetFullPath(Path.Combine(_options.WorktreeRoot, repositoryKey, $"pr-{pullRequestId}-r{reviewId}"));

    /// <summary>
    /// Removes the worktree and prunes the clone's worktree list. When git cannot remove the directory (locked files,
    /// paths too long for git) it is deleted directly, provided it lies under <c>Git:WorktreeRoot</c>.
    /// </summary>
    /// <remarks>
    /// git locks a worktree while <c>worktree add</c> runs, and the lock stays if the add is killed part-way. A locked
    /// worktree needs <c>--force</c> twice to be removed, and prune skips it, so a missing one is unlocked first.
    /// </remarks>
    private async Task<WorktreeRemoveResult> RemoveWorktreeAsync(ResolvedRepository repository, string worktreePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        string[] prune = ["worktree", "prune"];

        if (!Directory.Exists(worktreePath))
        {
            await RunAsync(repository.Key, repository.LocalPath, ["worktree", "unlock", worktreePath], cancellationToken);
            await RunAsync(repository.Key, repository.LocalPath, prune, cancellationToken);
            return WorktreeRemoveResult.Success("The folder was already gone; pruned the worktree list.");
        }

        var removed = await RunAsync(
            repository.Key, repository.LocalPath, ["worktree", "remove", "--force", "--force", worktreePath], cancellationToken);
        await RunAsync(repository.Key, repository.LocalPath, prune, cancellationToken);

        if (!Directory.Exists(worktreePath))
        {
            return WorktreeRemoveResult.Success("Removed with git worktree remove.");
        }

        var gitError = removed.Succeeded ? "git reported success but the folder remains" : Describe(removed);
        if (!IsUnderWorktreeRoot(worktreePath))
        {
            return WorktreeRemoveResult.Failure(
                $"git worktree remove failed ({gitError}) and '{worktreePath}' is outside Git:WorktreeRoot, so it is not deleted directly.");
        }

        try
        {
            DeleteDirectory(worktreePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Deleting {Path} after git worktree remove failed also failed: {Error}", worktreePath, ex.Message);
            return WorktreeRemoveResult.Failure($"git worktree remove failed ({gitError}); deleting the folder failed: {ex.Message}");
        }

        await RunAsync(repository.Key, repository.LocalPath, ["worktree", "unlock", worktreePath], cancellationToken);
        await RunAsync(repository.Key, repository.LocalPath, prune, cancellationToken);
        return WorktreeRemoveResult.Success($"git worktree remove failed ({gitError}); deleted the folder and pruned instead.");
    }

    private async Task<ProcessResult> RunAsync(string repositoryKey, string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await _gitRunner.RunAsync(workingDirectory, arguments, CommandTimeout, cancellationToken);
        var command = string.Join(' ', arguments);

        _logger.LogInformation(
            "git {Command} for {RepositoryKey}: exit code {ExitCode} in {ElapsedMs} ms",
            command, repositoryKey, result.ExitCode, (long)result.Duration.TotalMilliseconds);

        if (!result.Succeeded)
        {
            _logger.LogWarning("git {Command} for {RepositoryKey} failed: {Error}", command, repositoryKey, Describe(result));
        }

        return result;
    }

    private bool IsUnderWorktreeRoot(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.WorktreeRoot)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deletes a directory tree, first clearing the read-only attribute git sets on pack and object files.</summary>
    private static void DeleteDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        var entries = directory.EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
        });

        foreach (var entry in entries.Append(directory))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        directory.Delete(recursive: true);
    }

    private static string Describe(ProcessResult result)
    {
        if (result.TimedOut)
        {
            return $"timed out after {result.Duration.TotalMinutes:0.#} minutes";
        }

        var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return string.IsNullOrWhiteSpace(error) ? $"exit code {result.ExitCode}" : $"exit code {result.ExitCode}: {error.Trim()}";
    }

    private static string Describe(string[] remoteNames) => remoteNames.Length == 0 ? "none" : string.Join(", ", remoteNames);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? "no error output" : line;
    }
}
