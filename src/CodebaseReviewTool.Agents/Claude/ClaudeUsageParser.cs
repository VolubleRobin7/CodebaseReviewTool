using System.Globalization;
using System.Text.RegularExpressions;
using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Agents.Claude;

/// <summary>Reads the plan limits out of the text the CLI's <c>/usage</c> command prints.</summary>
internal static partial class ClaudeUsageParser
{
    // The reset time without a year ("Nov 18, 7:30pm", "Nov 25, 2pm") or, for a reset later today, without a date.
    private static readonly string[] _dateFormats = ["MMM d, h:mmtt", "MMM d, htt", "MMM d h:mmtt", "MMM d htt", "MMM d"];
    private static readonly string[] _timeFormats = ["h:mmtt", "htt"];

    /// <summary>
    /// Reads the lines shaped "Name: 12% used · resets Nov 11, 2:40pm (Australia/Sydney)". Every other line (the plan
    /// banner, the breakdown of what used the limits) is ignored. A reset time that cannot be read is left null.
    /// </summary>
    public static IReadOnlyList<AgentUsageLimit> Parse(string? text, DateTime nowUtc) =>
    [
        .. LimitLine().Matches(text ?? string.Empty).Select(m => new AgentUsageLimit(
            m.Groups["name"].Value.Trim(),
            Math.Clamp(double.Parse(m.Groups["percent"].Value, CultureInfo.InvariantCulture), 0, 100),
            m.Groups["resets"].Success ? ParseResetsAt(m.Groups["resets"].Value, m.Groups["zone"].Value, nowUtc) : null)),
    ];

    /// <summary>
    /// Converts the CLI's wording to UTC in the time zone it names (local time when it names none or an unknown one).
    /// The CLI gives no year, so the reset is taken as the next such time from now.
    /// </summary>
    private static DateTime? ParseResetsAt(string text, string zoneId, DateTime nowUtc)
    {
        var zone = FindZone(zoneId);
        var now = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var normalised = text.Trim().ToUpperInvariant();

        DateTime resets;
        if (DateTime.TryParseExact(normalised, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            resets = new DateTime(now.Year, date.Month, date.Day, date.Hour, date.Minute, 0, DateTimeKind.Unspecified);
            if (resets < now.AddDays(-1))
            {
                // A December reading of a January reset.
                resets = resets.AddYears(1);
            }
        }
        else if (DateTime.TryParseExact(normalised, _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            resets = DateTime.SpecifyKind(now.Date + time.TimeOfDay, DateTimeKind.Unspecified);
            if (resets < now)
            {
                resets = resets.AddDays(1);
            }
        }
        else
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(resets, zone);
        }
        catch (ArgumentException)
        {
            // A time skipped by a daylight-saving change.
            return null;
        }
    }

    private static TimeZoneInfo FindZone(string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    [GeneratedRegex(
        @"^[ \t]*(?<name>[^:\r\n]+?):[ \t]*(?<percent>\d+(?:\.\d+)?)%[ \t]*used(?:[ \t]*·[ \t]*resets[ \t]+(?<resets>[^(\r\n]+?))?(?:[ \t]*\((?<zone>[^)\r\n]*)\))?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LimitLine();
}
