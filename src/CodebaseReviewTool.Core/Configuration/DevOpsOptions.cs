using System.ComponentModel.DataAnnotations;
using CodebaseReviewTool.Core.DevOps;

namespace CodebaseReviewTool.Core.Configuration;

public sealed class DevOpsOptions
{
    public const string SectionName = "DevOps";

    /// <summary>
    /// e.g. <c>https://dev.azure.com/contoso</c>, <c>https://contoso.visualstudio.com</c>, or for Azure DevOps Server the
    /// collection URL <c>http://server:8080/tfs/Contoso</c> (the collection plays the part of the organisation).
    /// </summary>
    [Required(ErrorMessage = "DevOps:OrganizationUrl is required.")]
    [ConfirmNotPlaceholder]
    [Url(ErrorMessage = "DevOps:OrganizationUrl must be an absolute http(s) URL.")]
    public string OrganizationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Personal access token (Code (Read) scope). Lives in user-secrets only and is never displayed or logged.
    /// Not required at startup so the Settings page can report it as missing, and not used when
    /// <see cref="UseDefaultCredentials"/> is true.
    /// </summary>
    [ConfirmNotPlaceholder]
    public string? Pat { get; set; }

    /// <summary>
    /// REST <c>api-version</c> sent with every versioned call (e.g. <c>3.0</c> for TFS 2017, <c>7.1</c> for cloud).
    /// Null or empty means detect it once by probing the server.
    /// </summary>
    [ConfirmNotPlaceholder]
    [RegularExpression(@"^\d+\.\d+(-preview(\.\d+)?)?$", ErrorMessage = "DevOps:ApiVersion must look like 3.0, 7.1 or 7.1-preview.1.")]
    public string? ApiVersion { get; set; }

    /// <summary>
    /// On-prem only: authenticate with the signed-in Windows account (NTLM/Kerberos) instead of the PAT.
    /// </summary>
    public bool UseDefaultCredentials { get; set; }

    /// <summary>True when there is something to authenticate with (Windows credentials or a PAT).</summary>
    public bool HasCredentials => UseDefaultCredentials || !string.IsNullOrEmpty(Pat);

    /// <summary>"Windows" or "PAT"; for display.</summary>
    public string AuthMode => UseDefaultCredentials ? "Windows" : "PAT";

    [Range(10, 86400, ErrorMessage = "DevOps:PollIntervalSeconds must be between {1} and {2}.")]
    public int PollIntervalSeconds { get; set; } = 120;

    /// <summary>Organisation name taken from <see cref="OrganizationUrl"/>, or null when it is not a recognised form.</summary>
    public string? OrganizationName => AzureDevOpsUrl.TryParseOrganizationUrl(OrganizationUrl, out var name) ? name : null;
}
