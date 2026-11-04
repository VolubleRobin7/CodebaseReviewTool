namespace CodebaseReviewTool.Core.Configuration;

/// <summary>
/// Thrown during startup when configuration is well-formed but does not match the machine (e.g. a repository whose
/// remote cannot be resolved). Host prints <see cref="Failures"/> and exits.
/// </summary>
public sealed class StartupValidationException(IReadOnlyList<string> failures)
    : Exception("Startup validation failed: " + string.Join(" ", failures))
{
    public IReadOnlyList<string> Failures { get; } = failures;
}
