using System.ComponentModel.DataAnnotations;

namespace CodebaseReviewTool.Core.Configuration;

public sealed class ReviewOptions
{
    public const string SectionName = "Reviews";

    [Range(1, 8, ErrorMessage = "Reviews:MaxConcurrent must be between {1} and {2}.")]
    public int MaxConcurrent { get; set; } = 1;
}
