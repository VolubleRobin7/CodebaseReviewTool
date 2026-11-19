using System.ComponentModel.DataAnnotations;
using CodebaseReviewTool.Infrastructure.Configuration;

namespace CodebaseReviewTool.Agents.Claude;

public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    /// <summary>Full path of claude.exe. Environment variables are expanded at startup; the file must exist.</summary>
    [Required(ErrorMessage = "Claude:ExecutablePath is required.")]
    [ConfirmNotPlaceholder]
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Model passed with <c>--model</c> when a step sets none. Opus unless configuration overrides it.</summary>
    [Required(ErrorMessage = "Claude:DefaultModel is required.")]
    [ConfirmNotPlaceholder]
    public string DefaultModel { get; set; } = "opus";

    /// <summary>Models offered per step on the Configuration page. The default model is always offered.</summary>
    public string[] Models { get; set; } = [];

    /// <summary>
    /// The effort levels each model accepts, lowest first, keyed by model name; a model left out takes no effort
    /// setting. Each level must be one the CLI's <c>--effort</c> accepts (<see cref="EffortLevels"/>).
    /// </summary>
    public Dictionary<string, string[]> ModelEfforts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The levels <c>claude --effort</c> accepts, lowest first.</summary>
    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "xhigh", "max"];

    [Range(1, 1000, ErrorMessage = "Claude:MaxTurns must be between {1} and {2}.")]
    public int MaxTurns { get; set; } = 50;

    /// <summary>How often the home page's usage bar is refreshed with <c>/usage</c>; reviews also refresh it.</summary>
    [Range(1, 60, ErrorMessage = "Claude:UsageRefreshMinutes must be between {1} and {2}.")]
    public int UsageRefreshMinutes { get; set; } = 5;

    /// <summary>Passed to <c>--allowedTools</c>. No default list here: list binding appends to existing items.</summary>
    [MinLength(1, ErrorMessage = "Claude:AllowedTools must list at least one tool.")]
    public List<string> AllowedTools { get; set; } = [];
}
