using System.ComponentModel.DataAnnotations;

namespace AzureExploreAgent.Api.Options;

/// <summary>
/// Binds to the "A2A" configuration section. <see cref="PublicBaseUrl"/> is the externally reachable host
/// advertised in the Agent Card (e.g. the Container Apps / App Service URL).
/// </summary>
public sealed class A2AHostingOptions
{
    public const string SectionName = "A2A";

    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = string.Empty;
}
