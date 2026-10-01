using System.ComponentModel;
using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Application.AzureCli;

/// <summary>
/// The model-facing Azure CLI capability. Accepts a structured command (never a raw shell string), enforces
/// <see cref="AzureCliCommandPolicy"/>, runs the real <c>az</c> binary and returns sanitized, size-capped output.
/// </summary>
public sealed class AzureCliQueryTool(
    IAzureCliRunner runner,
    IOptions<AzureCliOptions> azureCliOptions,
    IOptions<ServicePrincipalOptions> servicePrincipalOptions,
    ILogger<AzureCliQueryTool> logger)
{
    public const string ToolName = "execute_azure_cli";

    private const string ToolDescription =
        "Executes a read-only Azure CLI query against the Azure scope this agent is authorized to see and returns the real JSON output. " +
        "Use it for any question about the actual Azure environment. Only list/show/get/check/query operations are allowed.";

    private const int MaxErrorCharacters = 4_000;

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(ExecuteAsync, name: ToolName, description: ToolDescription);

    public async Task<string> ExecuteAsync(
        [Description("Azure CLI command group path without the leading 'az', e.g. 'resource', 'webapp', 'network vnet', 'network private-endpoint'.")]
        string commandGroup,
        [Description("Read-only operation, e.g. 'list' or 'show'.")]
        string operation,
        [Description("Optional named arguments, e.g. {\"--resource-group\": \"rg-prod\"}. Use a null value for flags. Prefer '--query' (JMESPath) and '--resource-group' to narrow large results. Output is always JSON.")]
        Dictionary<string, string?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, arguments);

        if (!decision.IsAllowed)
        {
            logger.LogWarning(
                "Rejected Azure CLI command group {CommandGroup} operation {Operation}: {Reason}",
                commandGroup,
                operation,
                decision.Reason);

            return $"Azure CLI command rejected: {decision.Reason}";
        }

        logger.LogInformation("Executing Azure CLI command: az {CommandGroup} {Operation}", commandGroup, operation);

        var result = await runner.RunAsync(decision.Arguments, timeout: null, cancellationToken);
        var cli = azureCliOptions.Value;

        if (result.TimedOut)
        {
            logger.LogWarning("Azure CLI command timed out: az {CommandGroup} {Operation}", commandGroup, operation);

            return $"Azure CLI command timed out after {cli.TimeoutSeconds} seconds and was cancelled. " +
                   "Narrow the query (for example with --resource-group or --query) and try again.";
        }

        var secret = servicePrincipalOptions.Value.ClientSecret;

        if (result.ExitCode != 0)
        {
            return FormatFailure(result, secret);
        }

        var output = AzureCliOutputSanitizer.Sanitize(result.StandardOutput, secret);

        return AzureCliOutputSanitizer.Truncate(output, cli.MaxOutputCharacters);
    }

    private static string FormatFailure(AzureCliResult result, string secret)
    {
        var error = AzureCliOutputSanitizer.Truncate(
            AzureCliOutputSanitizer.Sanitize(result.StandardError, secret).Trim(),
            MaxErrorCharacters);

        var message = $"Azure CLI command failed.\n\nExit code: {result.ExitCode}\n\nAzure error:\n{error}";

        if (error.Contains("AuthorizationFailed", StringComparison.OrdinalIgnoreCase))
        {
            message += "\n\nThe configured service principal does not have permission to access the requested scope.";
        }

        return message;
    }
}
