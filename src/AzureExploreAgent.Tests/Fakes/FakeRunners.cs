using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Processes;

namespace AzureExploreAgent.Tests.Fakes;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<Func<ProcessRequest, CancellationToken, ProcessResult>> _responses = new();

    public List<ProcessRequest> Requests { get; } = [];

    public FakeProcessRunner Returns(ProcessResult result)
    {
        _responses.Enqueue((_, _) => result);
        return this;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Dequeue()(request, cancellationToken));
    }
}

internal sealed class FakeAzureCliRunner : IAzureCliRunner
{
    private readonly Queue<Func<CancellationToken, AzureCliResult>> _responses = new();

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public List<TimeSpan?> Timeouts { get; } = [];

    public FakeAzureCliRunner Returns(AzureCliResult result)
    {
        _responses.Enqueue(_ => result);
        return this;
    }

    public FakeAzureCliRunner ReturnsSuccess(string standardOutput = "") =>
        Returns(new AzureCliResult(0, standardOutput, string.Empty, TimedOut: false));

    public FakeAzureCliRunner Responds(Func<CancellationToken, AzureCliResult> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public Task<AzureCliResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        Timeouts.Add(timeout);
        return Task.FromResult(_responses.Dequeue()(cancellationToken));
    }
}
