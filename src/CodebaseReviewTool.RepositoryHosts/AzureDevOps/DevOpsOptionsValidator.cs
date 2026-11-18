using CodebaseReviewTool.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.RepositoryHosts.AzureDevOps;

/// <summary>Checks that <c>DevOps:OrganizationUrl</c> names an organisation in a recognised form.</summary>
public sealed class DevOpsOptionsValidator : IValidateOptions<DevOpsOptions>
{
    public ValidateOptionsResult Validate(string? name, DevOpsOptions options)
    {
        var url = options.OrganizationUrl;
        if (string.IsNullOrWhiteSpace(url) || ConfirmNotPlaceholderAttribute.IsPlaceholder(url) || options.OrganizationName is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"DevOps:OrganizationUrl: '{url}' is not of the form https://dev.azure.com/<organisation>, https://<organisation>.visualstudio.com or an Azure DevOps Server collection URL (http://server[:port]/[tfs/]<collection>).");
    }
}
