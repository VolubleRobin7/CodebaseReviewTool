using System.Reflection;
using System.Text.RegularExpressions;

namespace CodebaseReviewTool.Core.Prompts;

/// <summary>A prompt the tool ships for one flow step.</summary>
/// <param name="Step">The step number, starting at 0.</param>
/// <param name="Name">The step's name, as its card and report section are headed.</param>
/// <param name="Text">The prompt, with LF line endings and no trailing blank lines.</param>
public sealed record DefaultPrompt(int Step, string Name, string Text);

/// <summary>
/// The prompts the tool ships, one per flow step, read from the embedded <c>Prompts/Defaults/&lt;step&gt;-&lt;name&gt;.txt</c>
/// resources: the number before the dash is the step and the rest of the file name is the step's name. They seed the
/// flow on first run and are what "Reset to default" restores.
/// </summary>
public static partial class DefaultPrompts
{
    private const string ResourcePrefix = "CodebaseReviewTool.Core.Prompts.Defaults.";

    private static readonly Lazy<IReadOnlyDictionary<int, DefaultPrompt>> _prompts = new(Load);

    /// <summary>Step numbers that have a shipped prompt, in ascending order.</summary>
    public static IReadOnlyList<int> Steps => _prompts.Value.Keys.Order().ToList();

    /// <summary>The shipped prompt for <paramref name="step"/>, or null when the step has none.</summary>
    public static DefaultPrompt? For(int step) => _prompts.Value.GetValueOrDefault(step);

    private static Dictionary<int, DefaultPrompt> Load()
    {
        var assembly = typeof(DefaultPrompts).Assembly;
        var prompts = new Dictionary<int, DefaultPrompt>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var match = FileName().Match(name[ResourcePrefix.Length..]);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            // Stored with whatever line endings the repository uses; the database and the editor get LF.
            var step = int.Parse(match.Groups["step"].Value);
            prompts[step] = new DefaultPrompt(step, match.Groups["name"].Value, reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd());
        }

        return prompts;
    }

    [GeneratedRegex(@"^(?<step>\d+)-(?<name>[^.]+)\.txt$")]
    private static partial Regex FileName();
}
