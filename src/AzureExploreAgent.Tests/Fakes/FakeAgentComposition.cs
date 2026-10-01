using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Foundry;
using AzureExploreAgent.Infrastructure.Instructions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AzureExploreAgent.Tests.Fakes;

internal sealed class FakeFoundryAgentBuilder : IFoundryAgentBuilder
{
    public string? Model { get; private set; }
    public string? Name { get; private set; }
    public string? Instructions { get; private set; }
    public IList<AITool>? Tools { get; private set; }
    public AIAgent Agent { get; } = new ChatClientAgent(new StubChatClient());

    public AIAgent Build(string model, string name, string instructions, IList<AITool> tools)
    {
        Model = model;
        Name = name;
        Instructions = instructions;
        Tools = tools;
        return Agent;
    }
}

internal sealed class FakeAgentInstructionsProvider(string instructions) : IAgentInstructionsProvider
{
    public Task<string> GetInstructionsAsync(CancellationToken cancellationToken) => Task.FromResult(instructions);
}

internal sealed class FakeAzureCliAuthenticator : IAzureCliAuthenticator
{
    public Exception? Exception { get; init; }

    public int LoginCalls { get; private set; }

    public Task LoginAsync(CancellationToken cancellationToken)
    {
        LoginCalls++;
        return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
    }
}

internal sealed class StubChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Unit tests never call the model.");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Unit tests never call the model.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
