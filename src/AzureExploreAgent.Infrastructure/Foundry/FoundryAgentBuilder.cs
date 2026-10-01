using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AzureExploreAgent.Infrastructure.Foundry;

public interface IFoundryAgentBuilder
{
    AIAgent Build(string model, string name, string instructions, IList<AITool> tools);
}

public sealed class FoundryAgentBuilder(AIProjectClient projectClient, ILoggerFactory loggerFactory) : IFoundryAgentBuilder
{
    public AIAgent Build(string model, string name, string instructions, IList<AITool> tools) =>
        projectClient
            .AsAIAgent(
                model: model,
                name: name,
                instructions: instructions,
                tools: tools)
            .WithConsoleLogging(loggerFactory.CreateLogger("AzureExploreAgent.Agent"));
}
