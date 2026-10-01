using AzureExploreAgent.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Infrastructure.AzureCli;

public sealed class AzureCliRunner(IProcessRunner processRunner, IOptions<AzureCliOptions> options) : IAzureCliRunner
{
    public const string ConfigDirectoryVariable = "AZURE_CONFIG_DIR";

    public async Task<AzureCliResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var cliOptions = options.Value;
        var configDirectory = cliOptions.ResolveConfigDirectory();

        Directory.CreateDirectory(configDirectory);

        var request = new ProcessRequest(
            cliOptions.ExecutablePath,
            arguments,
            new Dictionary<string, string> { [ConfigDirectoryVariable] = configDirectory },
            timeout ?? TimeSpan.FromSeconds(cliOptions.TimeoutSeconds));

        var result = await processRunner.RunAsync(request, cancellationToken);

        return new AzureCliResult(result.ExitCode, result.StandardOutput, result.StandardError, result.TimedOut);
    }
}
