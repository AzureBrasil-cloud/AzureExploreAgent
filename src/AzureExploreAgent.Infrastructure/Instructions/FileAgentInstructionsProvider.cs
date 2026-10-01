using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Infrastructure.Instructions;

/// <summary>
/// Binds to the "AgentInstructions" configuration section. Instructions are kept in an editable file
/// instead of being embedded in code. Relative paths resolve against the application base directory.
/// </summary>
public sealed class AgentInstructionsOptions
{
    public const string SectionName = "AgentInstructions";

    [Required]
    public string FilePath { get; set; } = "Instructions/azure-explore-agent.md";
}

public interface IAgentInstructionsProvider
{
    Task<string> GetInstructionsAsync(CancellationToken cancellationToken);
}

public sealed class FileAgentInstructionsProvider(IOptions<AgentInstructionsOptions> options) : IAgentInstructionsProvider
{
    public async Task<string> GetInstructionsAsync(CancellationToken cancellationToken)
    {
        var path = Path.IsPathRooted(options.Value.FilePath)
            ? options.Value.FilePath
            : Path.Combine(AppContext.BaseDirectory, options.Value.FilePath);

        var instructions = await File.ReadAllTextAsync(path, cancellationToken);

        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new InvalidOperationException($"Agent instructions file '{path}' is empty.");
        }

        return instructions;
    }
}
