using System.Text.Json;
using AzureExploreAgent.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Infrastructure.AzureCli;

public interface IAzureCliAuthenticator
{
    Task LoginAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Signs the isolated Azure CLI session in as the AzureExploreAgent service principal. This is application
/// infrastructure: the model can never trigger it, and the argument list is never logged because it carries the secret.
/// </summary>
public sealed class AzureCliAuthenticator(
    IAzureCliRunner runner,
    IOptions<ServicePrincipalOptions> servicePrincipalOptions,
    IOptions<AzureCliOptions> azureCliOptions,
    ILogger<AzureCliAuthenticator> logger) : IAzureCliAuthenticator
{
    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        var servicePrincipal = servicePrincipalOptions.Value;
        var cli = azureCliOptions.Value;
        var loginTimeout = TimeSpan.FromSeconds(cli.LoginTimeoutSeconds);

        var login = await runner.RunAsync(
            [
                "login",
                "--service-principal",
                "--username", servicePrincipal.ClientId,
                // "=" form keeps secrets that start with "-" from being parsed as a flag.
                $"--password={servicePrincipal.ClientSecret}",
                "--tenant", servicePrincipal.TenantId,
                "--output", "none",
                "--only-show-errors",
            ],
            loginTimeout,
            cancellationToken);

        EnsureSucceeded(login, "Azure CLI service principal login failed.");

        var setSubscription = await runner.RunAsync(
            ["account", "set", "--subscription", cli.SubscriptionId, "--only-show-errors"],
            loginTimeout,
            cancellationToken);

        EnsureSucceeded(setSubscription, "Azure CLI could not select the configured subscription.");

        var account = await runner.RunAsync(
            ["account", "show", "--output", "json", "--only-show-errors"],
            loginTimeout,
            cancellationToken);

        EnsureSucceeded(account, "Azure CLI could not read the active account.");
        VerifyAccount(account.StandardOutput, servicePrincipal, cli.SubscriptionId);

        logger.LogInformation(
            "Azure CLI authenticated as service principal {ClientId} on subscription {SubscriptionId}.",
            servicePrincipal.ClientId,
            cli.SubscriptionId);
    }

    private void EnsureSucceeded(AzureCliResult result, string message)
    {
        if (result.TimedOut)
        {
            throw new AzureCliAuthenticationException($"{message} The command timed out.");
        }

        if (result.ExitCode != 0)
        {
            var error = AzureCliOutputSanitizer.Sanitize(result.StandardError, servicePrincipalOptions.Value.ClientSecret);
            throw new AzureCliAuthenticationException($"{message} Exit code {result.ExitCode}. {error.Trim()}");
        }
    }

    private static void VerifyAccount(string accountJson, ServicePrincipalOptions servicePrincipal, string subscriptionId)
    {
        string? tenantId, activeSubscriptionId, userName, userType;

        try
        {
            using var document = JsonDocument.Parse(accountJson);
            var root = document.RootElement;

            tenantId = root.GetProperty("tenantId").GetString();
            activeSubscriptionId = root.GetProperty("id").GetString();
            userName = root.GetProperty("user").GetProperty("name").GetString();
            userType = root.GetProperty("user").GetProperty("type").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AzureCliAuthenticationException("Azure CLI returned an unexpected 'account show' payload.", ex);
        }

        if (!string.Equals(tenantId, servicePrincipal.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(activeSubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(userName, servicePrincipal.ClientId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(userType, "servicePrincipal", StringComparison.OrdinalIgnoreCase))
        {
            throw new AzureCliAuthenticationException(
                "Azure CLI is not using the configured tenant, subscription and service principal.");
        }
    }
}

public sealed class AzureCliAuthenticationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
