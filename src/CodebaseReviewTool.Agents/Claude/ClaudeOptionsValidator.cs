using CodebaseReviewTool.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Agents.Claude;

/// <summary>Checks that <c>Claude:ExecutablePath</c> points at an existing file (after env-var expansion).</summary>
public sealed class ClaudeOptionsValidator : IValidateOptions<ClaudeOptions>
{
    public ValidateOptionsResult Validate(string? name, ClaudeOptions options)
    {
        var path = options.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || ConfirmNotPlaceholderAttribute.IsPlaceholder(path) || File.Exists(path))
        {
            // Empty and placeholder values are reported by the data-annotation validation.
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail($"Claude:ExecutablePath: file not found: '{path}'.");
    }
}
