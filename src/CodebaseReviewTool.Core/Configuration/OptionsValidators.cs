using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Configuration;

/// <summary>Checks that <c>Claude:ExecutablePath</c> points at an existing file (after env-var expansion).</summary>
public sealed class ClaudeOptionsValidator : IValidateOptions<ClaudeOptions>
{
    public ValidateOptionsResult Validate(string? name, ClaudeOptions options)
    {
        var path = options.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || IsPlaceholder(path) || File.Exists(path))
        {
            // Empty and placeholder values are reported by the data-annotation validation.
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail($"Claude:ExecutablePath: file not found: '{path}'.");
    }

    internal static bool IsPlaceholder(string value) => value.Contains('<') || value.Contains('>');
}

/// <summary>Checks that <c>DevOps:OrganizationUrl</c> names an organisation in a recognised form.</summary>
public sealed class DevOpsOptionsValidator : IValidateOptions<DevOpsOptions>
{
    public ValidateOptionsResult Validate(string? name, DevOpsOptions options)
    {
        var url = options.OrganizationUrl;
        if (string.IsNullOrWhiteSpace(url) || ClaudeOptionsValidator.IsPlaceholder(url) || options.OrganizationName is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"DevOps:OrganizationUrl: '{url}' is not of the form https://dev.azure.com/<organisation>, https://<organisation>.visualstudio.com or an Azure DevOps Server collection URL (http://server[:port]/[tfs/]<collection>).");
    }
}

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
