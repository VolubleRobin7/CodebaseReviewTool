using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Reporting;

/// <summary>Text formatting shared by the report page and the downloaded report, so both read the same.</summary>
public static class ReportFormat
{
    private static readonly JsonSerializerOptions _indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The severities in the order the report lists them, most severe first.</summary>
    public static IReadOnlyList<IssueSeverity> Severities { get; } = Enum.GetValues<IssueSeverity>();

    /// <summary>Formats a stored UTC time in the machine's local time, as "2 Nov 2026, 4:12 pm".</summary>
    public static string Timestamp(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.ToString("d MMM yyyy, h:mm ", CultureInfo.InvariantCulture) + (local.Hour < 12 ? "am" : "pm");
    }

    /// <summary>Formats a run's duration as "48 s" or "3 min 12 s".</summary>
    public static string Duration(long milliseconds)
    {
        var elapsed = TimeSpan.FromMilliseconds(milliseconds);
        return elapsed.TotalMinutes < 1
            ? $"{Math.Max(1, (int)Math.Round(elapsed.TotalSeconds))} s"
            : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";
    }

    /// <summary>Formats a cost in US dollars to the cent, or to four decimals for a run that cost less than a cent.</summary>
    public static string Cost(double usd) => usd.ToString(usd is > 0 and < 0.01 ? "$0.0000" : "$0.00", CultureInfo.InvariantCulture);

    /// <summary>Explains, at the top of a cancelled review's report, why some sections are missing.</summary>
    public const string CancelledNote =
        "This review was cancelled. Steps that finished before then are shown; the rest did not complete.";

    /// <summary>The tooltip beside a review's cost when <paramref name="steps"/> finished steps reported none.</summary>
    public static string UnknownCostNote(int steps) =>
        steps == 1
            ? "The cost may be higher: 1 step stopped before reporting what it spent."
            : $"The cost may be higher: {steps} steps stopped before reporting what they spent.";

    /// <summary>The CSS class that colours a severity (<c>sev-high</c> and so on).</summary>
    public static string SeverityClass(IssueSeverity severity) => "sev-" + severity.ToString().ToLowerInvariant();

    /// <summary>The issue's file with its line appended when known, as "src/Foo.cs:42".</summary>
    public static string Location(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue.Line is { } line ? $"{issue.File}:{line}" : issue.File;
    }

    /// <summary>Re-indents JSON for reading; returns the text unchanged when it is not valid JSON.</summary>
    public static string PrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, _indented);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
