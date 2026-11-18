using System.ComponentModel.DataAnnotations;
using CodebaseReviewTool.Infrastructure.Configuration;

namespace CodebaseReviewTool.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Path of the SQLite file. Environment variables are expanded at startup.</summary>
    [Required(ErrorMessage = "Database:Path is required.")]
    [ConfirmNotPlaceholder]
    public string Path { get; set; } = string.Empty;
}
