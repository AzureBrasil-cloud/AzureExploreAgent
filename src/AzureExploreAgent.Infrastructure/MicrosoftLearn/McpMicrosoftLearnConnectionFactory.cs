using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AzureExploreAgent.Infrastructure.MicrosoftLearn;

public sealed class McpMicrosoftLearnConnectionFactory(IOptions<MicrosoftLearnOptions> options) : IMicrosoftLearnConnectionFactory
{
    public async Task<IMicrosoftLearnConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var learnOptions = options.Value;

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(learnOptions.Endpoint),
            Name = "MicrosoftLearn",
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(learnOptions.ConnectionTimeoutSeconds),
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        return new McpMicrosoftLearnConnection(client);
    }

    private sealed class McpMicrosoftLearnConnection(McpClient client) : IMicrosoftLearnConnection
    {
        public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken)
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            return [.. tools];
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
