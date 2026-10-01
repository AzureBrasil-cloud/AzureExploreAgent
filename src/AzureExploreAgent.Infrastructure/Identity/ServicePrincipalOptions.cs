using System.ComponentModel.DataAnnotations;

namespace AzureExploreAgent.Infrastructure.Identity;

/// <summary>
/// Binds to the "ServicePrincipal" configuration section. Outbound identity of AzureExploreAgent, used for
/// Foundry model access and Azure CLI login. <see cref="ClientSecret"/> is a secret — never commit a real value;
/// supply it via user-secrets or environment variables.
/// </summary>
public sealed class ServicePrincipalOptions
{
    public const string SectionName = "ServicePrincipal";

    [Required]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;
}
