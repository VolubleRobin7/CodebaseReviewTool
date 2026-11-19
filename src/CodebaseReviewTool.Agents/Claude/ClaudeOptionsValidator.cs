using CodebaseReviewTool.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Agents.Claude;

/// <summary>
/// Checks what the data annotations cannot: that <c>Claude:ExecutablePath</c> points at an existing file (after env-var
/// expansion), and that <c>Claude:ModelEfforts</c> names only configured models and levels the CLI accepts.
/// </summary>
public sealed class ClaudeOptionsValidator : IValidateOptions<ClaudeOptions>
{
    public ValidateOptionsResult Validate(string? name, ClaudeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // Empty and placeholder paths are reported by the data-annotation validation.
        var path = options.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(path) && !ConfirmNotPlaceholderAttribute.IsPlaceholder(path) && !File.Exists(path))
        {
            failures.Add($"Claude:ExecutablePath: file not found: '{path}'.");
        }

        var models = options.Models.Append(options.DefaultModel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (model, efforts) in options.ModelEfforts)
        {
            if (!models.Contains(model))
            {
                failures.Add($"Claude:ModelEfforts: '{model}' is not the default model or one of Claude:Models.");
            }

            foreach (var effort in efforts.Where(e => !ClaudeOptions.EffortLevels.Contains(e, StringComparer.OrdinalIgnoreCase)))
            {
                failures.Add($"Claude:ModelEfforts:{model}: '{effort}' is not one of {string.Join(", ", ClaudeOptions.EffortLevels)}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
