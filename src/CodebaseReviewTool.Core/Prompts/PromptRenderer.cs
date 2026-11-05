using System.Text.RegularExpressions;

namespace CodebaseReviewTool.Core.Prompts;

/// <summary>A placeholder that prompt text may use, with what it is replaced by.</summary>
/// <param name="Name">The name between the braces, e.g. <c>PR_ID</c>.</param>
/// <param name="Description">One line saying what it is replaced by.</param>
public sealed record PromptPlaceholder(string Name, string Description)
{
    /// <summary>The placeholder as written in prompt text, e.g. <c>{{PR_ID}}</c>.</summary>
    public string Token => $"{{{{{Name}}}}}";
}

/// <summary>Result of rendering a prompt.</summary>
/// <param name="Text">The prompt with every known placeholder replaced.</param>
/// <param name="UnknownPlaceholders">Names of placeholders that are not known, left in the text as written; distinct, in order of appearance.</param>
public sealed record RenderedPrompt(string Text, IReadOnlyList<string> UnknownPlaceholders);

/// <summary>
/// Substitutes <c>{{NAME}}</c> placeholders in prompt text. Unknown placeholders are left as written and reported, so
/// the UI can flag them; the renderer never fails.
/// </summary>
public static partial class PromptRenderer
{
    /// <summary>Every placeholder the renderer knows, in the order the reference panel lists them.</summary>
    public static IReadOnlyList<PromptPlaceholder> Placeholders { get; } =
    [
        new("PR_ID", "The pull request's number."),
        new("PR_TITLE", "The pull request's title."),
        new("PR_DESCRIPTION", "The pull request's description."),
        new("PR_AUTHOR", "The display name of the pull request's author."),
        new("PR_URL", "Web link to the pull request in Azure DevOps."),
        new("REPO_KEY", "The repository's stable key (a slug of its display name)."),
        new("REPO_NAME", "The repository's display name."),
        new("SOURCE_BRANCH", "The branch being merged."),
        new("TARGET_BRANCH", "The branch it merges into."),
        new("SOURCE_COMMIT", "The commit the worktree is checked out at."),
        new("MERGE_BASE", "The merge base of the source commit and the target branch."),
        new("CHANGED_FILES", "One line per changed file: git status letters, a tab, then the path."),
        new("WORKTREE_PATH", "Full path of the worktree the step runs in."),
    ];

    /// <summary>Replaces the known placeholders in <paramref name="template"/> with values from <paramref name="context"/>.</summary>
    public static RenderedPrompt Render(string template, PromptContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        var values = Values(context);
        var unknown = new List<string>();

        var text = PlaceholderPattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (values.TryGetValue(name, out var value))
            {
                return value;
            }

            if (!unknown.Contains(name))
            {
                unknown.Add(name);
            }

            return match.Value;
        });

        return new RenderedPrompt(text, unknown);
    }

    /// <summary>Names of the placeholders in <paramref name="template"/> that are not known; distinct, in order of appearance.</summary>
    public static IReadOnlyList<string> FindUnknownPlaceholders(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        return PlaceholderPattern().Matches(template)
            .Select(m => m.Groups[1].Value)
            .Where(name => !Placeholders.Any(p => p.Name == name))
            .Distinct()
            .ToList();
    }

    private static Dictionary<string, string> Values(PromptContext context) => new(StringComparer.Ordinal)
    {
        ["PR_ID"] = context.PullRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["PR_TITLE"] = context.Title,
        ["PR_DESCRIPTION"] = context.Description,
        ["PR_AUTHOR"] = context.Author,
        ["PR_URL"] = context.Url,
        ["REPO_KEY"] = context.RepositoryKey,
        ["REPO_NAME"] = context.RepositoryName,
        ["SOURCE_BRANCH"] = context.SourceBranch,
        ["TARGET_BRANCH"] = context.TargetBranch,
        ["SOURCE_COMMIT"] = context.SourceCommit,
        ["MERGE_BASE"] = context.MergeBase,
        ["CHANGED_FILES"] = context.ChangedFiles,
        ["WORKTREE_PATH"] = context.WorktreePath,
    };

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}
