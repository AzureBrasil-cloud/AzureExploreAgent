using System.ComponentModel.DataAnnotations;

namespace AzureExploreAgent.Infrastructure.AzureCli;

/// <summary>
/// Binds to the "AzureCli" configuration section. Controls how the real <c>az</c> executable is invoked.
/// </summary>
public sealed class AzureCliOptions
{
    public const string SectionName = "AzureCli";

    [Required]
    public string ExecutablePath { get; set; } = "az";

    /// <summary>
    /// Isolated <c>AZURE_CONFIG_DIR</c> so the agent never shares a developer's personal Azure CLI session.
    /// Defaults to a folder under the system temp directory when empty.
    /// </summary>
    public string ConfigDirectory { get; set; } = string.Empty;

    [Required]
    public string SubscriptionId { get; set; } = string.Empty;

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    [Range(1, 600)]
    public int LoginTimeoutSeconds { get; set; } = 90;

    [Range(1_000, 1_000_000)]
    public int MaxOutputCharacters { get; set; } = 60_000;

    public string ResolveConfigDirectory() =>
        string.IsNullOrWhiteSpace(ConfigDirectory)
            ? Path.Combine(Path.GetTempPath(), "AzureExploreAgent", "azure-cli")
            : ConfigDirectory;
}
