using AzureExploreAgent.Application.AzureCli;
using AzureExploreAgent.Application.MicrosoftLearn;
using AzureExploreAgent.Infrastructure.Foundry;
using AzureExploreAgent.Infrastructure.Instructions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Application.Agents;

/// <summary>
/// Composes AzureExploreAgent: Foundry model + external instructions + Microsoft Learn MCP tools + Azure CLI tool.
/// </summary>
public sealed class AzureExploreAgentFactory(
    IAgentInstructionsProvider instructionsProvider,
    IMicrosoftLearnToolProvider microsoftLearnToolProvider,
    AzureCliQueryTool azureCliQueryTool,
    IFoundryAgentBuilder agentBuilder,
    IOptions<FoundryOptions> foundryOptions,
    ILogger<AzureExploreAgentFactory> logger)
{
    public const string AgentName = "AzureExploreAgent";

    public async Task<AIAgent> CreateAsync(CancellationToken cancellationToken)
    {
        var instructions = await instructionsProvider.GetInstructionsAsync(cancellationToken);
        var learnTools = await microsoftLearnToolProvider.GetToolsAsync(cancellationToken);

        List<AITool> tools = [.. learnTools, azureCliQueryTool.AsAIFunction()];
        var model = foundryOptions.Value.ModelDeploymentName;

        logger.LogInformation(
            "Creating {AgentName} on model {Model} with {ToolCount} tools: {ToolNames}",
            AgentName,
            model,
            tools.Count,
            string.Join(", ", tools.Select(tool => tool.Name)));

        return agentBuilder.Build(model, AgentName, instructions, tools);
    }
}
