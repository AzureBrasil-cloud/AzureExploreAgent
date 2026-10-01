using AzureExploreAgent.Application.AzureCli;
using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Identity;
using AzureExploreAgent.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Tests.Application.AzureCli;

public class AzureCliQueryToolTests
{
    private const string ClientSecret = "super-secret-value";

    [Fact]
    public async Task Returns_stdout_when_exit_code_is_zero()
    {
        const string json = """[{"name":"api-prod","type":"Microsoft.Web/sites"}]""";
        var runner = new FakeAzureCliRunner().ReturnsSuccess(json);
        var tool = CreateTool(runner);

        var result = await tool.ExecuteAsync("webapp", "list");

        Assert.Equal(json, result);
        Assert.Equal(["webapp", "list", "--output", "json", "--only-show-errors"], runner.Calls.Single());
    }

    [Fact]
    public async Task Returns_controlled_error_when_authorization_fails()
    {
        const string stderr = "ERROR: (AuthorizationFailed) The client 'x' does not have authorization to perform action 'Microsoft.Web/sites/read'.";
        var runner = new FakeAzureCliRunner().Returns(new AzureCliResult(1, string.Empty, stderr, TimedOut: false));
        var tool = CreateTool(runner);

        var result = await tool.ExecuteAsync("webapp", "list");

        Assert.StartsWith("Azure CLI command failed.", result);
        Assert.Contains("Exit code: 1", result);
        Assert.Contains("AuthorizationFailed", result);
        Assert.Contains("does not have permission to access the requested scope", result);
    }

    [Fact]
    public async Task Redacts_secrets_and_tokens_from_errors()
    {
        var stderr = $"ERROR: failed with {ClientSecret} and header Authorization: Bearer abc.def-ghi";
        var runner = new FakeAzureCliRunner().Returns(new AzureCliResult(2, string.Empty, stderr, TimedOut: false));
        var tool = CreateTool(runner);

        var result = await tool.ExecuteAsync("webapp", "list");

        Assert.DoesNotContain(ClientSecret, result);
        Assert.DoesNotContain("abc.def-ghi", result);
        Assert.Contains(AzureCliOutputSanitizer.Redacted, result);
    }

    [Fact]
    public async Task Returns_controlled_message_when_command_times_out()
    {
        var runner = new FakeAzureCliRunner().Returns(new AzureCliResult(-1, string.Empty, string.Empty, TimedOut: true));
        var tool = CreateTool(runner, timeoutSeconds: 30);

        var result = await tool.ExecuteAsync("resource", "list");

        Assert.Contains("timed out after 30 seconds", result);
        Assert.Contains("cancelled", result);
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        var runner = new FakeAzureCliRunner().Responds(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new AzureCliResult(0, "[]", string.Empty, TimedOut: false);
        });
        var tool = CreateTool(runner);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync("resource", "list", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Truncates_excessive_output()
    {
        var runner = new FakeAzureCliRunner().ReturnsSuccess(new string('x', 5_000));
        var tool = CreateTool(runner, maxOutputCharacters: 1_000);

        var result = await tool.ExecuteAsync("resource", "list");

        Assert.Equal(1_000 + AzureCliOutputSanitizer.TruncationMarker.Length, result.Length);
        Assert.EndsWith("[output truncated]", result);
    }

    [Fact]
    public async Task Rejects_blocked_command_without_running_azure_cli()
    {
        var runner = new FakeAzureCliRunner();
        var tool = CreateTool(runner);

        var result = await tool.ExecuteAsync("group", "delete", new Dictionary<string, string?> { ["--name"] = "rg-production" });

        Assert.StartsWith("Azure CLI command rejected:", result);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Rejects_shell_injection_without_running_azure_cli()
    {
        var runner = new FakeAzureCliRunner();
        var tool = CreateTool(runner);

        var result = await tool.ExecuteAsync("resource", "list ; env");

        Assert.StartsWith("Azure CLI command rejected:", result);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Is_exposed_as_an_ai_function_that_never_asks_for_a_cancellation_token()
    {
        var runner = new FakeAzureCliRunner().ReturnsSuccess("[]");
        var function = CreateTool(runner).AsAIFunction();

        Assert.Equal(AzureCliQueryTool.ToolName, function.Name);
        Assert.False(function.JsonSchema.GetProperty("properties").TryGetProperty("cancellationToken", out _));

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["commandGroup"] = "network vnet",
            ["operation"] = "list",
        });

        Assert.Equal("[]", result?.ToString());
        Assert.Equal(["network", "vnet", "list", "--output", "json", "--only-show-errors"], runner.Calls.Single());
    }

    private static AzureCliQueryTool CreateTool(FakeAzureCliRunner runner, int timeoutSeconds = 30, int maxOutputCharacters = 60_000) =>
        new(
            runner,
            Options.Create(new AzureCliOptions
            {
                SubscriptionId = "sub",
                TimeoutSeconds = timeoutSeconds,
                MaxOutputCharacters = maxOutputCharacters,
            }),
            Options.Create(new ServicePrincipalOptions { TenantId = "t", ClientId = "c", ClientSecret = ClientSecret }),
            NullLogger<AzureCliQueryTool>.Instance);
}
