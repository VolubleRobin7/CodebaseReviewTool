using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using CodebaseReviewTool.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Agents.Claude;

/// <summary>
/// Runs a step through the Claude Code CLI in print mode:
/// <c>claude -p --output-format json --allowedTools &lt;tool&gt;... --model m --max-turns n [--json-schema s]</c>.
/// The model and timeout come from the request, falling back to <c>Claude:DefaultModel</c> and
/// <c>Claude:TimeoutMinutes</c>; the tools and turn limit always come from configuration. The prompt goes through stdin
/// because Windows limits a command line to about 32,000 characters. The CLI prints one JSON object with <c>type</c>
/// "result", <c>is_error</c>, <c>result</c>, <c>session_id</c>, <c>total_cost_usd</c>, <c>duration_ms</c> and, with a
/// schema, <c>structured_output</c>; every field is read defensively.
/// </summary>
public sealed class ClaudeCodeRunner(IProcessRunner processRunner, IOptions<ClaudeOptions> options, ILogger<ClaudeCodeRunner> logger) : IAgentRunner
{
    private static readonly TimeSpan _usageTimeout = TimeSpan.FromMinutes(1);

    private readonly IProcessRunner _processRunner = processRunner;
    private readonly ClaudeOptions _options = options.Value;
    private readonly ILogger<ClaudeCodeRunner> _logger = logger;

    async Task<AgentResult> IAgentRunner.RunAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeout = request.Timeout ?? TimeSpan.FromMinutes(_options.TimeoutMinutes);
        var arguments = BuildArguments(request);
        var commandLine = FormatCommandLine(_options.ExecutablePath, arguments);

        var process = await _processRunner.RunAsync(
            new ProcessRequest(_options.ExecutablePath, arguments, request.WorkingDirectory, request.Prompt, timeout),
            cancellationToken);

        var measuredMs = (long)process.Duration.TotalMilliseconds;
        var output = FindResultObject(process.StandardOutput);

        var resultText = GetString(output, "result") ?? string.Empty;
        var structured = output?["structured_output"] is { } node && node.GetValueKind() != JsonValueKind.Null ? node.ToJsonString() : null;
        var sessionId = GetString(output, "session_id");
        var cost = GetDouble(output, "total_cost_usd");
        var durationMs = GetLong(output, "duration_ms") ?? measuredMs;
        var isError = GetBool(output, "is_error") ?? false;
        var subtype = GetString(output, "subtype");

        string? error = null;
        if (process.TimedOut)
        {
            var minutes = timeout.TotalMinutes;
            var unit = minutes == 1 ? "minute" : "minutes";
            error = $"Timed out after {minutes.ToString("0.#", CultureInfo.InvariantCulture)} {unit}; the process was stopped.";
        }
        else if (output is null)
        {
            error = $"The CLI's output is not a JSON result (exit code {process.ExitCode}).{Detail(process.StandardError)}";
        }
        else if (isError)
        {
            var reason = string.IsNullOrWhiteSpace(resultText) ? subtype ?? "no detail" : resultText.Trim();
            error = $"The CLI reported an error ({subtype ?? "error"}): {reason}";
        }
        else if (process.ExitCode != 0)
        {
            error = $"The CLI exited with code {process.ExitCode}.{Detail(process.StandardError)}";
        }

        var result = new AgentResult(
            Succeeded: error is null,
            ResultText: resultText,
            StructuredOutputJson: structured,
            SessionId: sessionId,
            CostUsd: cost,
            DurationMs: durationMs,
            RawOutput: process.StandardOutput,
            StandardError: process.StandardError,
            ExitCode: process.TimedOut ? null : process.ExitCode,
            TimedOut: process.TimedOut,
            Error: error,
            CommandLine: commandLine);

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Agent run in {WorkingDirectory} succeeded in {DurationMs} ms (session {SessionId}, cost {CostUsd} USD)",
                request.WorkingDirectory, durationMs, sessionId, cost);
        }
        else
        {
            _logger.LogWarning("Agent run in {WorkingDirectory} failed after {DurationMs} ms: {Error}", request.WorkingDirectory, measuredMs, error);
        }

        return result;
    }

    /// <summary>
    /// Runs <c>claude -p /usage --output-format json</c>, a local command that costs no tokens, and reads the plan
    /// limits out of the text it prints.
    /// </summary>
    async Task<ServiceResult<AgentUsage>> IAgentRunner.GetUsageAsync(CancellationToken cancellationToken)
    {
        // An empty stdin is closed at once, so the CLI never waits on it. The working directory is irrelevant to a
        // local command; the temp folder keeps it out of any repository's project settings.
        var process = await _processRunner.RunAsync(
            new ProcessRequest(_options.ExecutablePath, ["-p", "/usage", "--output-format", "json"], Path.GetTempPath(), string.Empty, _usageTimeout),
            cancellationToken);

        var output = FindResultObject(process.StandardOutput);
        var text = GetString(output, "result");

        if (process.TimedOut)
        {
            return ServiceResult<AgentUsage>.Failure($"/usage did not finish within {_usageTimeout.TotalSeconds:0} seconds.");
        }

        if (text is null || (GetBool(output, "is_error") ?? false))
        {
            var detail = text ?? process.StandardError;
            return ServiceResult<AgentUsage>.Failure($"/usage failed (exit code {process.ExitCode}). {detail.Trim()}".Trim());
        }

        return ServiceResult<AgentUsage>.Success(ClaudeUsageParser.Parse(text, DateTime.UtcNow));
    }

    private List<string> BuildArguments(AgentRequest request)
    {
        var arguments = new List<string> { "-p", "--output-format", "json", "--allowedTools" };
        arguments.AddRange(_options.AllowedTools);

        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        arguments.Add("--max-turns");
        arguments.Add(_options.MaxTurns.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            arguments.Add("--json-schema");
            arguments.Add(Compact(request.JsonSchema));
        }

        return arguments;
    }

    /// <summary>Removes the schema's formatting so the argument is one line; text that is not JSON is passed unchanged.</summary>
    private static string Compact(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.ToJsonString() ?? json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// The whole of stdout when it is one JSON object; otherwise (line-delimited output) the last line that is a JSON
    /// object with <c>type</c> "result". Null when neither is found.
    /// </summary>
    private static JsonObject? FindResultObject(string stdout)
    {
        if (TryParseObject(stdout) is { } whole)
        {
            return whole;
        }

        JsonObject? last = null;
        foreach (var line in stdout.Split('\n'))
        {
            if (TryParseObject(line) is { } candidate && GetString(candidate, "type") == "result")
            {
                last = candidate;
            }
        }

        return last;
    }

    private static JsonObject? TryParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? GetDouble(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    private static long? GetLong(JsonObject? json, string name)
    {
        if (json?[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var whole))
        {
            return whole;
        }

        return value.TryGetValue<double>(out var number) ? (long)number : null;
    }

    private static bool? GetBool(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static string Detail(string standardError) =>
        string.IsNullOrWhiteSpace(standardError) ? string.Empty : $" {standardError.Trim()}";

    /// <summary>The command line as a shell would show it, quoting arguments that contain spaces or quotes.</summary>
    private static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
        string.Join(' ', new[] { fileName }.Concat(arguments).Select(Quote));

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"')
            ? argument
            : $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
