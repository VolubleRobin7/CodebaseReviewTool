using System.ComponentModel.DataAnnotations;

namespace CodebaseReviewTool.Core.Configuration;

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

    [Range(1, 1440, ErrorMessage = "Claude:TimeoutMinutes must be between {1} and {2}.")]
    public int TimeoutMinutes { get; set; } = 20;

    [Range(1, 1000, ErrorMessage = "Claude:MaxTurns must be between {1} and {2}.")]
    public int MaxTurns { get; set; } = 50;

    /// <summary>Passed to <c>--allowedTools</c>. No default list here: list binding appends to existing items.</summary>
    [MinLength(1, ErrorMessage = "Claude:AllowedTools must list at least one tool.")]
    public List<string> AllowedTools { get; set; } = [];
}
