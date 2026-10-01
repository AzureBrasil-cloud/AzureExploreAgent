using AzureExploreAgent.Application.MicrosoftLearn;
using AzureExploreAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureExploreAgent.Tests.Application.MicrosoftLearn;

public class MicrosoftLearnToolProviderTests
{
    [Fact]
    public async Task Returns_tools_discovered_from_the_mcp_server()
    {
        var connection = new FakeMicrosoftLearnConnection
        {
            Tools = [FakeTools.Named("microsoft_docs_search"), FakeTools.Named("microsoft_docs_fetch")],
        };
        await using var provider = CreateProvider(new FakeMicrosoftLearnConnectionFactory { Connection = connection });

        var tools = await provider.GetToolsAsync(CancellationToken.None);

        Assert.Equal(["microsoft_docs_search", "microsoft_docs_fetch"], tools.Select(tool => tool.Name));
        Assert.False(connection.IsDisposed);
    }

    [Fact]
    public async Task Returns_empty_collection_when_server_exposes_no_tools()
    {
        await using var provider = CreateProvider(new FakeMicrosoftLearnConnectionFactory());

        var tools = await provider.GetToolsAsync(CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task Returns_empty_collection_when_connection_fails()
    {
        var factory = new FakeMicrosoftLearnConnectionFactory { ConnectException = new HttpRequestException("unreachable") };
        await using var provider = CreateProvider(factory);

        var tools = await provider.GetToolsAsync(CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task Returns_empty_collection_and_closes_connection_when_tool_discovery_fails()
    {
        var connection = new FakeMicrosoftLearnConnection { ListToolsException = new InvalidOperationException("tools/list failed") };
        await using var provider = CreateProvider(new FakeMicrosoftLearnConnectionFactory { Connection = connection });

        var tools = await provider.GetToolsAsync(CancellationToken.None);

        Assert.Empty(tools);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        var factory = new FakeMicrosoftLearnConnectionFactory { ConnectException = new OperationCanceledException() };
        await using var provider = CreateProvider(factory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetToolsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Keeps_connection_open_and_reuses_tools_until_disposed()
    {
        var connection = new FakeMicrosoftLearnConnection { Tools = [FakeTools.Named("microsoft_docs_search")] };
        var factory = new FakeMicrosoftLearnConnectionFactory { Connection = connection };
        var provider = CreateProvider(factory);

        var first = await provider.GetToolsAsync(CancellationToken.None);
        var second = await provider.GetToolsAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, factory.ConnectCalls);
        Assert.False(connection.IsDisposed);

        await provider.DisposeAsync();

        Assert.True(connection.IsDisposed);
    }

    private static MicrosoftLearnToolProvider CreateProvider(FakeMicrosoftLearnConnectionFactory factory) =>
        new(factory, NullLogger<MicrosoftLearnToolProvider>.Instance);
}
