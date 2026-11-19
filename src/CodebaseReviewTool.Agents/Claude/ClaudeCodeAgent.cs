using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using CodebaseReviewTool.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Agents.Claude;

/// <summary>
/// Claude Code, driven through its CLI in print mode:
/// <c>claude -p --output-format json --allowedTools &lt;tool&gt;... --model m [--effort e] --max-turns n [--json-schema s] [--resume id]</c>.
/// The model, effort and timeout come from the request; the tools and turn limit always come from configuration. The
/// prompt goes through stdin because Windows limits a command line to about 32,000 characters. The CLI prints one JSON
/// object with <c>type</c> "result", <c>is_error</c>, <c>result</c>, <c>session_id</c>, <c>total_cost_usd</c>,
/// <c>duration_ms</c>, <c>modelUsage</c> and, with a schema, <c>structured_output</c>; every field is read defensively.
/// </summary>
public sealed class ClaudeCodeAgent(IProcessRunner processRunner, IOptions<ClaudeOptions> options, ILogger<ClaudeCodeAgent> logger) : IAgent
{
    private static readonly TimeSpan _usageTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _healthCheckTimeout = TimeSpan.FromSeconds(60);

    private readonly IProcessRunner _processRunner = processRunner;
    private readonly ClaudeOptions _options = options.Value;
    private readonly ILogger<ClaudeCodeAgent> _logger = logger;

    string IAgent.Key => "claude";

    string IAgent.DisplayName => "Claude Code";

    // The configured default comes first, which makes it the agent's default.
    IReadOnlyList<AgentModel> IAgent.Models =>
    [
        .. _options.Models
            .Prepend(_options.DefaultModel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new AgentModel(name, EffortsFor(name))),
    ];

    async Task<ServiceResult<string>> IAgent.HealthCheckAsync(CancellationToken cancellationToken)
    {
        var process = await _processRunner.RunAsync(
            new ProcessRequest(_options.ExecutablePath, ["--version"], Timeout: _healthCheckTimeout),
            cancellationToken);

        if (process.TimedOut)
        {
            return ServiceResult<string>.Failure($"Timed out after {process.Duration.TotalSeconds:0} s");
        }

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(process.StandardError) ? process.StandardOutput : process.StandardError;
            return ServiceResult<string>.Failure($"Exit code {process.ExitCode}: {error.Trim()}");
        }

        return ServiceResult<string>.Success(process.StandardOutput.Trim());
    }

    async Task<PromptResult> IAgent.RunPromptAsync(PromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var arguments = BuildArguments(request);
        var commandLine = FormatCommandLine(_options.ExecutablePath, arguments);

        var process = await _processRunner.RunAsync(
            new ProcessRequest(_options.ExecutablePath, arguments, request.WorkingDirectory, request.Prompt, request.Timeout),
            cancellationToken);

        var output = FindResultObject(process.StandardOutput);

        var text = GetString(output, "result") ?? string.Empty;
        var structured = output?["structured_output"] is { } node && node.GetValueKind() != JsonValueKind.Null ? node.ToJsonString() : null;
        var conversationId = GetString(output, "session_id");
        var cost = GetDouble(output, "total_cost_usd");
        var duration = GetLong(output, "duration_ms") is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : process.Duration;
        var isError = GetBool(output, "is_error") ?? false;
        var subtype = GetString(output, "subtype");

        string? error = null;
        if (process.TimedOut)
        {
            var minutes = request.Timeout.TotalMinutes;
            var length = minutes switch
            {
                < 1 => $"{request.Timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds",
                1 => "1 minute",
                _ => $"{minutes.ToString("0.#", CultureInfo.InvariantCulture)} minutes",
            };
            error = $"Timed out after {length}; the process was stopped.";
        }
        else if (output is null)
        {
            error = $"The CLI's output is not a JSON result (exit code {process.ExitCode}).{Detail(process.StandardError)}";
        }
        else if (isError)
        {
            var reason = string.IsNullOrWhiteSpace(text) ? subtype ?? "no detail" : text.Trim();
            error = $"The CLI reported an error ({subtype ?? "error"}): {reason}";
        }
        else if (process.ExitCode != 0)
        {
            error = $"The CLI exited with code {process.ExitCode}.{Detail(process.StandardError)}";
        }

        var result = new PromptResult(
            Error: error,
            Text: text,
            StructuredOutputJson: structured,
            ConversationId: conversationId,
            Model: FindModel(output),
            Effort: null, // The CLI's output does not say which effort level it ran at.
            CostUsd: cost,
            Duration: duration,
            RawOutput: process.StandardOutput,
            Diagnostics: Diagnostics(commandLine, process));

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Claude Code run in {WorkingDirectory} succeeded in {Duration} (conversation {ConversationId}, model {Model}, cost {CostUsd} USD)",
                request.WorkingDirectory, duration, conversationId, result.Model, cost);
        }
        else
        {
            _logger.LogWarning("Claude Code run in {WorkingDirectory} failed after {Duration}: {Error}", request.WorkingDirectory, process.Duration, error);
        }

        return result;
    }

    /// <summary>
    /// Runs <c>claude -p /usage --output-format json</c>, a local command that costs no tokens, and reads the plan
    /// limits out of the text it prints.
    /// </summary>
    async Task<ServiceResult<IReadOnlyList<AgentUsageLimit>>> IAgent.GetUsageAsync(CancellationToken cancellationToken)
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
            return ServiceResult<IReadOnlyList<AgentUsageLimit>>.Failure($"/usage did not finish within {_usageTimeout.TotalSeconds:0} seconds.");
        }

        if (text is null || (GetBool(output, "is_error") ?? false))
        {
            var detail = text ?? process.StandardError;
            return ServiceResult<IReadOnlyList<AgentUsageLimit>>.Failure($"/usage failed (exit code {process.ExitCode}). {detail.Trim()}".Trim());
        }

        return ServiceResult<IReadOnlyList<AgentUsageLimit>>.Success(ClaudeUsageParser.Parse(text, DateTime.UtcNow));
    }

    private List<string> BuildArguments(PromptRequest request)
    {
        var arguments = new List<string> { "-p", "--output-format", "json", "--allowedTools" };
        arguments.AddRange(_options.AllowedTools);

        arguments.Add("--model");
        arguments.Add(request.Model);

        if (!string.IsNullOrWhiteSpace(request.Effort))
        {
            arguments.Add("--effort");
            arguments.Add(request.Effort);
        }

        arguments.Add("--max-turns");
        arguments.Add(_options.MaxTurns.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            arguments.Add("--json-schema");
            arguments.Add(Compact(request.JsonSchema));
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationId))
        {
            arguments.Add("--resume");
            arguments.Add(request.ConversationId);
        }

        return arguments;
    }

    /// <summary>The model's levels from <c>Claude:ModelEfforts</c>, lowest first; none when it is not listed there.</summary>
    private IReadOnlyList<string> EffortsFor(string model) =>
        _options.ModelEfforts.FirstOrDefault(e => e.Key.Equals(model, StringComparison.OrdinalIgnoreCase)).Value is { } efforts
            ? [.. ClaudeOptions.EffortLevels.Where(level => efforts.Contains(level, StringComparer.OrdinalIgnoreCase))]
            : [];

    /// <summary>
    /// The model that did the work. <c>modelUsage</c> lists every model the run used, keyed by its full name, which
    /// includes any small model the CLI used on the side; the one that cost the most is the one that answered.
    /// </summary>
    private static string? FindModel(JsonObject? output) =>
        output?["modelUsage"] is JsonObject usage
            ? usage.OrderByDescending(entry => GetDouble(entry.Value as JsonObject, "costUSD") ?? 0).Select(entry => entry.Key).FirstOrDefault()
            : null;

    /// <summary>The command line, exit code and stderr, which are what explain a failed run of the CLI.</summary>
    private static string Diagnostics(string commandLine, ProcessResult process)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Command: {commandLine}")
            .AppendLine(process.TimedOut ? "Exit code: none (stopped on timeout)" : $"Exit code: {process.ExitCode}");

        if (!string.IsNullOrWhiteSpace(process.StandardError))
        {
            text.AppendLine().AppendLine("Standard error:").AppendLine(process.StandardError.Trim());
        }

        return text.ToString().TrimEnd();
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
