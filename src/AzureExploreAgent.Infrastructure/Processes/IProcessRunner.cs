namespace AzureExploreAgent.Infrastructure.Processes;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);
