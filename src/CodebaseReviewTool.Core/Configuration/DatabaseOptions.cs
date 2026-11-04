using System.ComponentModel.DataAnnotations;

namespace CodebaseReviewTool.Core.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Path of the SQLite file. Environment variables are expanded at startup.</summary>
    [Required(ErrorMessage = "Database:Path is required.")]
    [ConfirmNotPlaceholder]
    public string Path { get; set; } = string.Empty;
}
