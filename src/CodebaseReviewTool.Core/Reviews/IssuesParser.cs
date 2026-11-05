using System.Text.Json;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Reviews;

/// <summary>Outcome of parsing the issues step's output.</summary>
/// <param name="Issues">The parsed issues, without review ids; empty when parsing failed.</param>
/// <param name="Error">Why parsing failed, or null when it succeeded.</param>
public sealed record IssuesParseResult(IReadOnlyList<Issue> Issues, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Turns the issues step's output into <see cref="Issue"/> entities. Prefers the agent's structured output; otherwise
/// takes the first top-level JSON object in the result text, so a code fence or a sentence around it does no harm.
/// Never throws on bad input.
/// </summary>
public static class IssuesParser
{
    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static IssuesParseResult Parse(string? structuredOutputJson, string? resultText)
    {
        if (!string.IsNullOrWhiteSpace(structuredOutputJson))
        {
            var structured = TryParse(structuredOutputJson);
            if (structured.Succeeded)
            {
                return structured;
            }
        }

        if (string.IsNullOrWhiteSpace(resultText))
        {
            return Fail("The issues step returned no structured output and no text.");
        }

        var json = ExtractFirstObject(resultText);
        return json is null
            ? Fail("The issues step's output contains no JSON object.")
            : TryParse(json);
    }

    /// <summary>Returns the first balanced top-level <c>{...}</c> in <paramref name="text"/>, honouring strings and escapes.</summary>
    internal static string? ExtractFirstObject(string text)
    {
        for (var start = text.IndexOf('{'); start >= 0; start = text.IndexOf('{', start + 1))
        {
            var end = FindObjectEnd(text, start);
            if (end < 0)
            {
                return null;
            }

            var candidate = text[start..(end + 1)];
            if (IsJsonObject(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int FindObjectEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;

                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static bool IsJsonObject(string candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(candidate, _documentOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IssuesParseResult TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, _documentOptions);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail($"Expected a JSON object but found {root.ValueKind}.");
            }

            if (!TryGetProperty(root, "issues", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return Fail("The JSON object has no \"issues\" array.");
            }

            var issues = new List<Issue>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    issues.Add(ToIssue(item));
                }
            }

            return new IssuesParseResult(issues, null);
        }
        catch (JsonException ex)
        {
            return Fail($"The issues JSON is not valid: {ex.Message}");
        }
    }

    private static Issue ToIssue(JsonElement item)
    {
        var description = GetString(item, "description") ?? string.Empty;
        var severityText = GetString(item, "severity");

        if (!Enum.TryParse<IssueSeverity>(severityText, ignoreCase: true, out var severity) || !Enum.IsDefined(severity))
        {
            severity = IssueSeverity.Info;
            var note = string.IsNullOrWhiteSpace(severityText)
                ? "(No severity was given; shown as Info.)"
                : $"(Unknown severity \"{severityText}\"; shown as Info.)";
            description = string.IsNullOrEmpty(description) ? note : $"{description} {note}";
        }

        return new Issue
        {
            Severity = severity,
            Category = GetString(item, "category") ?? string.Empty,
            File = GetString(item, "file") ?? string.Empty,
            Line = GetLine(item),
            Title = GetString(item, "title") ?? string.Empty,
            Description = description,
            Suggestion = GetString(item, "suggestion"),
        };
    }

    private static int? GetLine(JsonElement item)
    {
        if (!TryGetProperty(item, "line", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var line) && line > 0 => line,
            JsonValueKind.String when int.TryParse(value.GetString(), out var line) && line > 0 => line,
            _ => null,
        };
    }

    private static string? GetString(JsonElement item, string name)
    {
        if (!TryGetProperty(item, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    /// <summary>Finds a property by name, ignoring case.</summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static IssuesParseResult Fail(string error) => new([], error);
}
