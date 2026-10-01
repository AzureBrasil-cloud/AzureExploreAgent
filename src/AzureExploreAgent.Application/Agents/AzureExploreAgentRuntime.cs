using AzureExploreAgent.Infrastructure.AzureCli;
using Microsoft.Agents.AI;

namespace AzureExploreAgent.Application.Agents;

/// <summary>
/// Holds the single AzureExploreAgent instance. Initialization is async (Azure CLI login, MCP discovery), so it runs
/// once at startup before endpoints are mapped, instead of inside a synchronous DI factory.
/// </summary>
public sealed class AzureExploreAgentRuntime(
    IAzureCliAuthenticator azureCliAuthenticator,
    AzureExploreAgentFactory agentFactory)
{
    private AIAgent? _agent;

    public AIAgent Agent =>
        _agent ?? throw new InvalidOperationException(
            $"{AzureExploreAgentFactory.AgentName} has not been initialized. Call {nameof(InitializeAsync)} during startup.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await azureCliAuthenticator.LoginAsync(cancellationToken);

        _agent = await agentFactory.CreateAsync(cancellationToken);
    }
}
