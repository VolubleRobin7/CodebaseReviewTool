using System.ComponentModel.DataAnnotations;

namespace CodebaseReviewTool.Core.Configuration;

/// <summary>One entry of the <c>Repositories</c> array. Validated by <see cref="RepositoriesOptionsValidator"/>.</summary>
public sealed class RepositoryOptions
{
    [Required(ErrorMessage = "DisplayName is required.")]
    [ConfirmNotPlaceholder]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Existing local clone. Environment variables are expanded at startup; the directory must exist.</summary>
    [Required(ErrorMessage = "LocalPath is required.")]
    [ConfirmNotPlaceholder]
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>Optional: remote to read; defaults to <c>origin</c>, else the only remote.</summary>
    [ConfirmNotPlaceholder]
    public string? Remote { get; set; }

    /// <summary>Optional: DevOps project, for remote URLs the parser cannot read.</summary>
    [ConfirmNotPlaceholder]
    public string? Project { get; set; }

    /// <summary>Optional: DevOps repository name, for remote URLs the parser cannot read.</summary>
    [ConfirmNotPlaceholder]
    public string? Repository { get; set; }
}

/// <summary>
/// The root <c>Repositories</c> array. A wrapper class (rather than <c>IOptions&lt;List&lt;T&gt;&gt;</c>) so it carries
/// validation and a post-configure step like every other section.
/// </summary>
public sealed class RepositoriesOptions
{
    public const string SectionName = "Repositories";

    [MinLength(1, ErrorMessage = "Repositories must contain at least one entry.")]
    public List<RepositoryOptions> Items { get; set; } = [];
}
