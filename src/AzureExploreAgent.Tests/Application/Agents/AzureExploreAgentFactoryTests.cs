using AzureExploreAgent.Application.Agents;
using AzureExploreAgent.Application.AzureCli;
using AzureExploreAgent.Application.MicrosoftLearn;
using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Foundry;
using AzureExploreAgent.Infrastructure.Identity;
using AzureExploreAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Tests.Application.Agents;

internal static class AzureExploreAgentTestFactory
{
    public const string Model = "gpt-4.1-mini";
    public const string Instructions = "# AzureExploreAgent instructions";

    public static AzureExploreAgentFactory Create(
        FakeFoundryAgentBuilder builder,
        FakeMicrosoftLearnConnectionFactory? learnConnectionFactory = null)
    {
        var azureCliQueryTool = new AzureCliQueryTool(
            new FakeAzureCliRunner(),
            Options.Create(new AzureCliOptions { SubscriptionId = "sub" }),
            Options.Create(new ServicePrincipalOptions { TenantId = "t", ClientId = "c", ClientSecret = "s" }),
            NullLogger<AzureCliQueryTool>.Instance);

        var learnToolProvider = new MicrosoftLearnToolProvider(
            learnConnectionFactory ?? new FakeMicrosoftLearnConnectionFactory(),
            NullLogger<MicrosoftLearnToolProvider>.Instance);

        return new AzureExploreAgentFactory(
            new FakeAgentInstructionsProvider(Instructions),
            learnToolProvider,
            azureCliQueryTool,
            builder,
            Options.Create(new FoundryOptions { ProjectEndpoint = "https://example.services.ai.azure.com/api/projects/p", ModelDeploymentName = Model }),
            NullLogger<AzureExploreAgentFactory>.Instance);
    }
}

public class AzureExploreAgentFactoryTests
{
    [Fact]
    public async Task Builds_agent_with_configured_model_name_and_instructions()
    {
        var builder = new FakeFoundryAgentBuilder();
        var factory = AzureExploreAgentTestFactory.Create(builder);

        var agent = await factory.CreateAsync(CancellationToken.None);

        Assert.Same(builder.Agent, agent);
        Assert.Equal(AzureExploreAgentTestFactory.Model, builder.Model);
        Assert.Equal(AzureExploreAgentFactory.AgentName, builder.Name);
        Assert.Equal(AzureExploreAgentTestFactory.Instructions, builder.Instructions);
    }

    [Fact]
    public async Task Includes_microsoft_learn_tools_and_azure_cli_tool()
    {
        var builder = new FakeFoundryAgentBuilder();
        var learn = new FakeMicrosoftLearnConnectionFactory
        {
            Connection = new FakeMicrosoftLearnConnection
            {
                Tools = [FakeTools.Named("microsoft_docs_search"), FakeTools.Named("microsoft_code_sample_search")],
            },
        };
        var factory = AzureExploreAgentTestFactory.Create(builder, learn);

        await factory.CreateAsync(CancellationToken.None);

        Assert.NotNull(builder.Tools);
        Assert.Equal(
            ["microsoft_docs_search", "microsoft_code_sample_search", AzureCliQueryTool.ToolName],
            builder.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task Still_includes_azure_cli_tool_when_microsoft_learn_is_unavailable()
    {
        var builder = new FakeFoundryAgentBuilder();
        var learn = new FakeMicrosoftLearnConnectionFactory { ConnectException = new HttpRequestException("unreachable") };
        var factory = AzureExploreAgentTestFactory.Create(builder, learn);

        await factory.CreateAsync(CancellationToken.None);

        Assert.NotNull(builder.Tools);
        Assert.Equal([AzureCliQueryTool.ToolName], builder.Tools.Select(tool => tool.Name));
    }
}

public class AzureExploreAgentRuntimeTests
{
    [Fact]
    public void Agent_is_unavailable_before_initialization()
    {
        var runtime = new AzureExploreAgentRuntime(
            new FakeAzureCliAuthenticator(),
            AzureExploreAgentTestFactory.Create(new FakeFoundryAgentBuilder()));

        Assert.Throws<InvalidOperationException>(() => runtime.Agent);
    }

    [Fact]
    public async Task Signs_in_to_azure_cli_and_builds_the_agent()
    {
        var authenticator = new FakeAzureCliAuthenticator();
        var builder = new FakeFoundryAgentBuilder();
        var runtime = new AzureExploreAgentRuntime(authenticator, AzureExploreAgentTestFactory.Create(builder));

        await runtime.InitializeAsync(CancellationToken.None);

        Assert.Equal(1, authenticator.LoginCalls);
        Assert.Same(builder.Agent, runtime.Agent);
    }

    [Fact]
    public async Task Does_not_build_the_agent_when_azure_cli_login_fails()
    {
        var authenticator = new FakeAzureCliAuthenticator { Exception = new AzureCliAuthenticationException("login failed") };
        var builder = new FakeFoundryAgentBuilder();
        var runtime = new AzureExploreAgentRuntime(authenticator, AzureExploreAgentTestFactory.Create(builder));

        await Assert.ThrowsAsync<AzureCliAuthenticationException>(() => runtime.InitializeAsync(CancellationToken.None));

        Assert.Null(builder.Model);
        Assert.Throws<InvalidOperationException>(() => runtime.Agent);
    }
}
