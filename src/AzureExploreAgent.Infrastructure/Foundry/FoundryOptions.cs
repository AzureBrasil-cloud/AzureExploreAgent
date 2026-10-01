using System.ComponentModel.DataAnnotations;

namespace AzureExploreAgent.Infrastructure.Foundry;

/// <summary>
/// Binds to the "Foundry" configuration section. The Foundry project used for model inference only —
/// the agent definition, instructions and tools live in this application (Responses Agent / direct inference).
/// </summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Foundry";

    /// <summary>e.g. <c>https://&lt;resource&gt;.services.ai.azure.com/api/projects/&lt;project&gt;</c>.</summary>
    [Required]
    [Url]
    public string ProjectEndpoint { get; set; } = string.Empty;

    [Required]
    public string ModelDeploymentName { get; set; } = string.Empty;
}
