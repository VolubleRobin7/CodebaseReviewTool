using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Git;

/// <summary>
/// Creates, inspects and removes the worktree each review runs in. Worktrees live under <c>Git:WorktreeRoot</c> as
/// <c>&lt;repositoryKey&gt;/pr-&lt;pullRequestId&gt;-r&lt;reviewId&gt;</c> and are detached at the pull request's source
/// commit, so a branch checked out elsewhere never blocks one. Every git command runs with
/// <c>Git:CommandTimeoutMinutes</c> and is logged with its elapsed time.
/// </summary>
public sealed class WorktreeService(GitRunner gitRunner, IOptions<GitOptions> options, ILogger<WorktreeService> logger)
{
    private const string _headsPrefix = "refs/heads/";

    private readonly GitRunner _gitRunner = gitRunner;
    private readonly GitOptions _options = options.Value;
    private readonly ILogger<WorktreeService> _logger = logger;

    private TimeSpan CommandTimeout => TimeSpan.FromMinutes(_options.CommandTimeoutMinutes);

    /// <summary>The directory a review's worktree is created in.</summary>
    public string GetWorktreePath(string repositoryKey, int pullRequestId, int reviewId) =>
        Path.GetFullPath(Path.Combine(_options.WorktreeRoot, repositoryKey, $"pr-{pullRequestId}-r{reviewId}"));

    /// <summary>
    /// Fetches (when <c>Git:FetchBeforeReview</c>), checks the source commit exists, adds a detached worktree at it and
    /// works out the merge base with the remote-tracking target branch. Throws <see cref="WorktreeException"/> with a
    /// readable message when any of that fails; a worktree added before the failure is removed again.
    /// </summary>
    public async Task<WorktreeInfo> CreateAsync(
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
            await RemoveAsync(repository, path, cancellationToken);
        }

        string[] add = ["worktree", "add", "--detach", path, sourceCommit];
        var added = await RunAsync(repository.Key, repository.LocalPath, add, cancellationToken);
        if (!added.Succeeded)
        {
            // A stale registration (directory deleted by hand, worktree still registered) also blocks the add.
            await RemoveAsync(repository, path, cancellationToken);
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
            await RemoveAsync(repository, path, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Lists the files changed between <paramref name="mergeBase"/> and <paramref name="sourceCommit"/>.</summary>
    public async Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
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

    /// <summary>
    /// Removes the worktree and prunes the clone's worktree list. When git cannot remove the directory (locked files,
    /// paths too long for git) it is deleted directly, provided it lies under <c>Git:WorktreeRoot</c>. A path that is
    /// already gone counts as removed. Expected failures are returned, not thrown.
    /// </summary>
    /// <remarks>
    /// git locks a worktree while <c>worktree add</c> runs, and the lock stays if the add is killed part-way. A locked
    /// worktree needs <c>--force</c> twice to be removed, and prune skips it, so a missing one is unlocked first.
    /// </remarks>
    public async Task<WorktreeRemoveResult> RemoveAsync(ResolvedRepository repository, string worktreePath, CancellationToken cancellationToken)
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
}
