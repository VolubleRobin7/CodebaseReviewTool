using System.ComponentModel.DataAnnotations;

namespace CodebaseReviewTool.Application.Configuration;

public sealed class ReviewOptions
{
    public const string SectionName = "Reviews";

    [Range(1, 8, ErrorMessage = "Reviews:MaxConcurrent must be between {1} and {2}.")]
    public int MaxConcurrent { get; set; } = 1;

    /// <summary>How long a flow step may run before its agent is stopped, when the step sets no timeout of its own.</summary>
    [Range(1, 1440, ErrorMessage = "Reviews:StepTimeoutMinutes must be between {1} and {2}.")]
    public int StepTimeoutMinutes { get; set; } = 20;
}
