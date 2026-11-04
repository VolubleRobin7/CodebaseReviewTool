using System.Reflection;
using System.Text.RegularExpressions;

namespace CodebaseReviewTool.Core.Prompts;

/// <summary>
/// The prompts the tool ships, one per flow step, read from the embedded <c>Prompts/Defaults/&lt;step&gt;-*.txt</c>
/// resources. They seed the flow on first run and are what "Reset to default" restores.
/// </summary>
public static partial class DefaultPrompts
{
    private const string ResourcePrefix = "CodebaseReviewTool.Core.Prompts.Defaults.";

    private static readonly Lazy<IReadOnlyDictionary<int, string>> _prompts = new(Load);

    /// <summary>Step numbers that have a shipped prompt, in ascending order.</summary>
    public static IReadOnlyList<int> Steps => _prompts.Value.Keys.Order().ToList();

    /// <summary>The shipped prompt for <paramref name="step"/>, or null when the step has none.</summary>
    public static string? For(int step) => _prompts.Value.GetValueOrDefault(step);

    private static Dictionary<int, string> Load()
    {
        var assembly = typeof(DefaultPrompts).Assembly;
        var prompts = new Dictionary<int, string>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var match = StepNumber().Match(name[ResourcePrefix.Length..]);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            // Stored with whatever line endings the repository uses; the database and the editor get LF.
            prompts[int.Parse(match.Groups[1].Value)] = reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd();
        }

        return prompts;
    }

    [GeneratedRegex(@"^(\d+)-")]
    private static partial Regex StepNumber();
}
