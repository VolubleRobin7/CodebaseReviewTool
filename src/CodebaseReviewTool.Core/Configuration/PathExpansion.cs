namespace CodebaseReviewTool.Core.Configuration;

/// <summary>
/// Expands <c>%ENV%</c> references in path-like configuration values. Applied through
/// <c>PostConfigure</c> so every consumer sees the expanded value.
/// </summary>
public static class PathExpansion
{
    public static string Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(value.Trim());
    }
}
