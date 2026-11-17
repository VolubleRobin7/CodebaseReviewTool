using System.Text.RegularExpressions;

namespace CodebaseReviewTool.Core.Agents;

/// <summary>One plan limit as the CLI's <c>/usage</c> command reports it.</summary>
/// <param name="Name">The limit's name, e.g. "Current session" or "Current week (all models)".</param>
/// <param name="PercentUsed">How much of the limit is used, 0 to 100.</param>
/// <param name="Resets">When the limit resets, as the CLI words it without the time zone; null when not given.</param>
public sealed record ClaudeUsageLimit(string Name, double PercentUsed, string? Resets);

/// <summary>The plan limits read by one run of <c>/usage</c>. Empty when the CLI is not on a subscription (an API key).</summary>
/// <param name="Limits">Every limit the command listed, in its order.</param>
/// <param name="CheckedUtc">When the command ran.</param>
public sealed partial record ClaudeUsageSnapshot(IReadOnlyList<ClaudeUsageLimit> Limits, DateTime CheckedUtc)
{
    /// <summary>The rolling session limit, which is the one a run of reviews uses up first; null when not listed.</summary>
    public ClaudeUsageLimit? Session =>
        Limits.FirstOrDefault(l => l.Name.Contains("session", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the lines of <c>/usage</c> output shaped "Name: 12% used · resets Nov 11, 2:40pm (Australia/Sydney)".
    /// Every other line (the plan banner, the breakdown of what used the limits) is ignored.
    /// </summary>
    public static ClaudeUsageSnapshot Parse(string text, DateTime checkedUtc)
    {
        var limits = LimitLine().Matches(text ?? string.Empty)
            .Select(m => new ClaudeUsageLimit(
                m.Groups["name"].Value.Trim(),
                Math.Clamp(double.Parse(m.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture), 0, 100),
                m.Groups["resets"].Success ? m.Groups["resets"].Value.Trim() : null))
            .ToList();

        return new ClaudeUsageSnapshot(limits, checkedUtc);
    }

    [GeneratedRegex(
        @"^[ \t]*(?<name>[^:\r\n]+?):[ \t]*(?<percent>\d+(?:\.\d+)?)%[ \t]*used(?:[ \t]*·[ \t]*resets[ \t]+(?<resets>[^(\r\n]+?))?(?:[ \t]*\([^)\r\n]*\))?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LimitLine();
}
