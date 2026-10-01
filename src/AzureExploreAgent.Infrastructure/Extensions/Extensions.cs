using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using AzureExploreAgent.Infrastructure.AzureCli;
using AzureExploreAgent.Infrastructure.Foundry;
using AzureExploreAgent.Infrastructure.Identity;
using AzureExploreAgent.Infrastructure.Instructions;
using AzureExploreAgent.Infrastructure.MicrosoftLearn;
using AzureExploreAgent.Infrastructure.Processes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AzureExploreAgent.Infrastructure.Extensions;

public static class Extensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Identity: explicit service principal, never ambient developer credentials.
        services.AddValidatedOptions<ServicePrincipalOptions>(configuration, ServicePrincipalOptions.SectionName);
        services.AddSingleton<TokenCredential>(provider =>
        {
            var servicePrincipal = provider.GetRequiredService<IOptions<ServicePrincipalOptions>>().Value;
            return new ClientSecretCredential(servicePrincipal.TenantId, servicePrincipal.ClientId, servicePrincipal.ClientSecret);
        });

        // Foundry (model inference)
        services.AddValidatedOptions<FoundryOptions>(configuration, FoundryOptions.SectionName);
        services.AddSingleton(provider => new AIProjectClient(
            new Uri(provider.GetRequiredService<IOptions<FoundryOptions>>().Value.ProjectEndpoint),
            provider.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IFoundryAgentBuilder, FoundryAgentBuilder>();

        // Microsoft Learn MCP
        services.AddValidatedOptions<MicrosoftLearnOptions>(configuration, MicrosoftLearnOptions.SectionName);
        services.AddSingleton<IMicrosoftLearnConnectionFactory, McpMicrosoftLearnConnectionFactory>();

        // Azure CLI
        services.AddValidatedOptions<AzureCliOptions>(configuration, AzureCliOptions.SectionName);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IAzureCliRunner, AzureCliRunner>();
        services.AddSingleton<IAzureCliAuthenticator, AzureCliAuthenticator>();

        // Agent instructions
        services.AddValidatedOptions<AgentInstructionsOptions>(configuration, AgentInstructionsOptions.SectionName);
        services.AddSingleton<IAgentInstructionsProvider, FileAgentInstructionsProvider>();

        return services;
    }

    private static void AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
