namespace CodebaseReviewTool.Application.Common.Exceptions;

/// <summary>A worktree could not be prepared; the message is written for the user.</summary>
public sealed class WorktreeException(string message) : Exception(message);
