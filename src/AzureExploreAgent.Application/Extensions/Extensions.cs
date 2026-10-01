using AzureExploreAgent.Application.Agents;
using AzureExploreAgent.Application.AzureCli;
using AzureExploreAgent.Application.MicrosoftLearn;
using AzureExploreAgent.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AzureExploreAgent.Application.Extensions;

public static class Extensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Infrastructure (identity, Foundry, Microsoft Learn MCP, Azure CLI, instructions).
        // Never call AddInfrastructure again from a host — options would be bound twice.
        services.AddInfrastructure(configuration);

        // Azure CLI tool
        services.AddSingleton<AzureCliQueryTool>();

        // Microsoft Learn MCP tools (singleton: owns the MCP session until shutdown)
        services.AddSingleton<IMicrosoftLearnToolProvider, MicrosoftLearnToolProvider>();

        // Agent composition
        services.AddSingleton<AzureExploreAgentFactory>();
        services.AddSingleton<AzureExploreAgentRuntime>();

        return services;
    }
}
