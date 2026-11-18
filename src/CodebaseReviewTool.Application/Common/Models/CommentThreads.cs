namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>A comment thread to create on a pull request.</summary>
/// <param name="Content">The comment's text, in markdown.</param>
/// <param name="FilePath">
/// The file to anchor the thread to, relative to the repository root (either slash); null or empty for a thread on the
/// pull request as a whole.
/// </param>
/// <param name="Line">The 1-based line in the file's latest version to anchor to; null for the whole file.</param>
public sealed record NewCommentThread(string Content, string? FilePath, int? Line);

/// <summary>A comment thread the host created.</summary>
/// <param name="Id">The host's id for the thread.</param>
/// <param name="IsAnchored">
/// True when the thread is anchored to its file; false when it had no file, or the host refused the anchor and it was
/// posted on the pull request as a whole.
/// </param>
public sealed record PostedCommentThread(int Id, bool IsAnchored);
