namespace AzureExploreAgent.Infrastructure.AzureCli;

public interface IAzureCliRunner
{
    /// <param name="arguments">Arguments passed to <c>az</c>, excluding the executable itself.</param>
    /// <param name="timeout">Overrides <see cref="AzureCliOptions.TimeoutSeconds"/> when provided.</param>
    Task<AzureCliResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout,
        CancellationToken cancellationToken);
}

public sealed record AzureCliResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);
