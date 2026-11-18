using System.Globalization;
using System.Text.RegularExpressions;
using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Agents.Claude;

/// <summary>Reads the plan limits out of the text the CLI's <c>/usage</c> command prints.</summary>
internal static partial class ClaudeUsageParser
{
    /// <summary>
    /// Reads the lines shaped "Name: 12% used · resets Nov 11, 2:40pm (Australia/Sydney)". Every other line (the plan
    /// banner, the breakdown of what used the limits) is ignored.
    /// </summary>
    public static AgentUsage Parse(string? text, DateTime checkedUtc)
    {
        var limits = LimitLine().Matches(text ?? string.Empty)
            .Select(m => new AgentUsageLimit(
                m.Groups["name"].Value.Trim(),
                Math.Clamp(double.Parse(m.Groups["percent"].Value, CultureInfo.InvariantCulture), 0, 100),
                m.Groups["resets"].Success ? m.Groups["resets"].Value.Trim() : null))
            .ToList();

        return new AgentUsage(limits, checkedUtc);
    }

    [GeneratedRegex(
        @"^[ \t]*(?<name>[^:\r\n]+?):[ \t]*(?<percent>\d+(?:\.\d+)?)%[ \t]*used(?:[ \t]*·[ \t]*resets[ \t]+(?<resets>[^(\r\n]+?))?(?:[ \t]*\([^)\r\n]*\))?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LimitLine();
}
