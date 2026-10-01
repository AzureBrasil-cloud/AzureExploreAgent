using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Identity;
using AzureExploreAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Tests.Infrastructure.AzureCli;

public class AzureCliAuthenticatorTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string ClientId = "22222222-2222-2222-2222-222222222222";
    private const string ClientSecret = "-starts-with-hyphen-secret";
    private const string SubscriptionId = "33333333-3333-3333-3333-333333333333";

    private static readonly string AccountJson = $$"""
        {
          "id": "{{SubscriptionId}}",
          "tenantId": "{{TenantId}}",
          "user": { "name": "{{ClientId}}", "type": "servicePrincipal" }
        }
        """;

    [Fact]
    public async Task Logs_in_as_service_principal_selects_subscription_and_verifies_account()
    {
        var runner = new FakeAzureCliRunner().ReturnsSuccess().ReturnsSuccess().ReturnsSuccess(AccountJson);
        var authenticator = CreateAuthenticator(runner);

        await authenticator.LoginAsync(CancellationToken.None);

        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(
            ["login", "--service-principal", "--username", ClientId, $"--password={ClientSecret}", "--tenant", TenantId, "--output", "none", "--only-show-errors"],
            runner.Calls[0]);
        Assert.Equal(["account", "set", "--subscription", SubscriptionId, "--only-show-errors"], runner.Calls[1]);
        Assert.Equal(["account", "show", "--output", "json", "--only-show-errors"], runner.Calls[2]);
        Assert.All(runner.Timeouts, timeout => Assert.Equal(TimeSpan.FromSeconds(90), timeout));
    }

    [Fact]
    public async Task Fails_without_leaking_secret_when_login_fails()
    {
        var runner = new FakeAzureCliRunner()
            .Returns(new AzureCliResult(1, string.Empty, $"ERROR: AADSTS7000215: Invalid client secret {ClientSecret}", TimedOut: false));
        var authenticator = CreateAuthenticator(runner);

        var exception = await Assert.ThrowsAsync<AzureCliAuthenticationException>(() => authenticator.LoginAsync(CancellationToken.None));

        Assert.DoesNotContain(ClientSecret, exception.Message);
        Assert.Contains("AADSTS7000215", exception.Message);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Fails_when_login_times_out()
    {
        var runner = new FakeAzureCliRunner().Returns(new AzureCliResult(-1, string.Empty, string.Empty, TimedOut: true));
        var authenticator = CreateAuthenticator(runner);

        var exception = await Assert.ThrowsAsync<AzureCliAuthenticationException>(() => authenticator.LoginAsync(CancellationToken.None));

        Assert.Contains("timed out", exception.Message);
    }

    [Fact]
    public async Task Fails_when_subscription_cannot_be_selected()
    {
        var runner = new FakeAzureCliRunner()
            .ReturnsSuccess()
            .Returns(new AzureCliResult(1, string.Empty, "ERROR: The subscription could not be found.", TimedOut: false));
        var authenticator = CreateAuthenticator(runner);

        await Assert.ThrowsAsync<AzureCliAuthenticationException>(() => authenticator.LoginAsync(CancellationToken.None));
        Assert.Equal(2, runner.Calls.Count);
    }

    [Theory]
    [InlineData("99999999-9999-9999-9999-999999999999", SubscriptionId, ClientId, "servicePrincipal")]
    [InlineData(TenantId, "99999999-9999-9999-9999-999999999999", ClientId, "servicePrincipal")]
    [InlineData(TenantId, SubscriptionId, "developer@contoso.com", "user")]
    public async Task Fails_when_active_account_is_not_the_configured_identity(string tenantId, string subscriptionId, string userName, string userType)
    {
        var accountJson = $$"""{ "id": "{{subscriptionId}}", "tenantId": "{{tenantId}}", "user": { "name": "{{userName}}", "type": "{{userType}}" } }""";
        var runner = new FakeAzureCliRunner().ReturnsSuccess().ReturnsSuccess().ReturnsSuccess(accountJson);
        var authenticator = CreateAuthenticator(runner);

        await Assert.ThrowsAsync<AzureCliAuthenticationException>(() => authenticator.LoginAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Fails_when_account_payload_is_unexpected()
    {
        var runner = new FakeAzureCliRunner().ReturnsSuccess().ReturnsSuccess().ReturnsSuccess("not-json");
        var authenticator = CreateAuthenticator(runner);

        await Assert.ThrowsAsync<AzureCliAuthenticationException>(() => authenticator.LoginAsync(CancellationToken.None));
    }

    private static AzureCliAuthenticator CreateAuthenticator(FakeAzureCliRunner runner) =>
        new(
            runner,
            Options.Create(new ServicePrincipalOptions { TenantId = TenantId, ClientId = ClientId, ClientSecret = ClientSecret }),
            Options.Create(new AzureCliOptions { SubscriptionId = SubscriptionId, LoginTimeoutSeconds = 90 }),
            NullLogger<AzureCliAuthenticator>.Instance);
}
