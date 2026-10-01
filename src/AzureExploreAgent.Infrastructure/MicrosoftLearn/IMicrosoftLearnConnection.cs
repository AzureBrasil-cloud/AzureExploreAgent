using Microsoft.Extensions.AI;

namespace AzureExploreAgent.Infrastructure.MicrosoftLearn;

public interface IMicrosoftLearnConnectionFactory
{
    Task<IMicrosoftLearnConnection> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A live MCP session. The returned tools call back into this session, so it must stay open
/// for as long as the agent can invoke them.
/// </summary>
public interface IMicrosoftLearnConnection : IAsyncDisposable
{
    Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken);
}
