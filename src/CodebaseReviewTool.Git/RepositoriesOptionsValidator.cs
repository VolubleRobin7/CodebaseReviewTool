using System.ComponentModel.DataAnnotations;
using CodebaseReviewTool.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Git;

/// <summary>
/// Validates each <c>Repositories[n]</c> entry: its data annotations (with keys such as <c>Repositories:0:LocalPath</c>
/// in the messages) and that <c>LocalPath</c> is an existing directory. The data-annotation validator does not
/// descend into list items, hence this class.
/// </summary>
public sealed class RepositoriesOptionsValidator : IValidateOptions<RepositoriesOptions>
{
    public ValidateOptionsResult Validate(string? name, RepositoriesOptions options)
    {
        var failures = new List<string>();

        for (var i = 0; i < options.Items.Count; i++)
        {
            var item = options.Items[i];
            var prefix = $"{RepositoriesOptions.SectionName}:{i}";

            var context = new ValidationContext(item, new Dictionary<object, object?> { [ConfirmNotPlaceholderAttribute.KeyPrefixItem] = prefix });
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(item, context, results, validateAllProperties: true))
            {
                foreach (var result in results)
                {
                    var message = result.ErrorMessage ?? "is invalid.";
                    failures.Add(message.StartsWith(prefix, StringComparison.Ordinal) ? message : $"{prefix}:{message}");
                }

                continue;
            }

            if (!Directory.Exists(item.LocalPath))
            {
                failures.Add($"{prefix}:LocalPath: directory not found: '{item.LocalPath}'.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
