using System.ComponentModel.DataAnnotations;

namespace AzureExploreAgent.Infrastructure.MicrosoftLearn;

/// <summary>
/// Binds to the "MicrosoftLearn" configuration section. The official Microsoft Learn remote MCP server (Streamable HTTP).
/// </summary>
public sealed class MicrosoftLearnOptions
{
    public const string SectionName = "MicrosoftLearn";

    [Required]
    [Url]
    public string Endpoint { get; set; } = "https://learn.microsoft.com/api/mcp";

    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;
}
