using AzureExploreAgent.Infrastructure.MicrosoftLearn;
using Microsoft.Extensions.AI;

namespace AzureExploreAgent.Tests.Fakes;

internal sealed class FakeMicrosoftLearnConnectionFactory : IMicrosoftLearnConnectionFactory
{
    public Exception? ConnectException { get; init; }

    public FakeMicrosoftLearnConnection Connection { get; init; } = new();

    public int ConnectCalls { get; private set; }

    public Task<IMicrosoftLearnConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectCalls++;

        if (ConnectException is not null)
        {
            throw ConnectException;
        }

        return Task.FromResult<IMicrosoftLearnConnection>(Connection);
    }
}

internal sealed class FakeMicrosoftLearnConnection : IMicrosoftLearnConnection
{
    public IReadOnlyList<AITool> Tools { get; init; } = [];

    public Exception? ListToolsException { get; init; }

    public bool IsDisposed { get; private set; }

    public Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken)
    {
        if (ListToolsException is not null)
        {
            throw ListToolsException;
        }

        return Task.FromResult(Tools);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class FakeTools
{
    public static AITool Named(string name) => AIFunctionFactory.Create(() => name, name: name);
}
