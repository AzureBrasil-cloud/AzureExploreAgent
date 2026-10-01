using AzureExploreAgent.Infrastructure.MicrosoftLearn;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AzureExploreAgent.Application.MicrosoftLearn;

public interface IMicrosoftLearnToolProvider
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Discovers Microsoft Learn MCP tools dynamically and owns the MCP session for the lifetime of the application,
/// so the returned tools stay usable by the agent. If Learn is unreachable, the agent still starts without its tools.
/// </summary>
public sealed class MicrosoftLearnToolProvider(
    IMicrosoftLearnConnectionFactory connectionFactory,
    ILogger<MicrosoftLearnToolProvider> logger) : IMicrosoftLearnToolProvider, IAsyncDisposable
{
    private IMicrosoftLearnConnection? _connection;
    private IReadOnlyList<AITool>? _tools;

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        IMicrosoftLearnConnection? connection = null;

        try
        {
            connection = await connectionFactory.ConnectAsync(cancellationToken);
            var tools = await connection.ListToolsAsync(cancellationToken);

            _connection = connection;
            _tools = tools;

            logger.LogInformation(
                "Discovered {ToolCount} Microsoft Learn MCP tools: {ToolNames}",
                tools.Count,
                string.Join(", ", tools.Select(tool => tool.Name)));

            return tools;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Microsoft Learn MCP is unavailable. The agent will run without Microsoft Learn tools.");

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }

            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
