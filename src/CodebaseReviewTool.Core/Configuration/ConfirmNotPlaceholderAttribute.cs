using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace CodebaseReviewTool.Core.Configuration;

/// <summary>
/// Rejects a value that still contains <c>&lt;</c> or <c>&gt;</c>, i.e. one of the placeholders shipped in
/// appsettings.json that should have been overridden in user-secrets. The message names the configuration key
/// but never echoes the value, so it is safe on secret properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfirmNotPlaceholderAttribute : ValidationAttribute
{
    /// <summary>
    /// Key in <see cref="ValidationContext.Items"/> that sets the configuration key prefix (used for array entries
    /// such as <c>Repositories:0</c>). Without it the prefix is the options class's <c>SectionName</c> constant.
    /// </summary>
    public const string KeyPrefixItem = "ConfigKeyPrefix";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || (!text.Contains('<') && !text.Contains('>')))
        {
            return ValidationResult.Success;
        }

        var key = ConfigKey(validationContext);
        return new ValidationResult(
            $"{key} contains '<' or '>', so it is still an unreplaced placeholder; set a real value (normally in user-secrets).",
            validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }

    private static string ConfigKey(ValidationContext context)
    {
        var prefix = context.Items.TryGetValue(KeyPrefixItem, out var item) && item is string fromItems
            ? fromItems
            : context.ObjectType.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;

        return string.IsNullOrEmpty(prefix) ? context.MemberName ?? "?" : $"{prefix}:{context.MemberName}";
    }
}
