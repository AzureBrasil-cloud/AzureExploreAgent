using A2A;
using A2A.AspNetCore;
using AzureExploreAgent.Api.Authorization;
using AzureExploreAgent.Api.Options;
using AzureExploreAgent.Application.Agents;
using AzureExploreAgent.Application.Extensions;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace AzureExploreAgent.Api.Extensions;

public static class ConfigurationExtensions
{
    public const string AgentKey = "azure-explore-agent";
    public const string A2APath = "/a2a/azure-explore-agent";

    public static void AddConfigurations(this WebApplicationBuilder builder, IConfiguration configuration)
    {
        var services = builder.Services;

        var tenantId = configuration["AzureAd:TenantId"];
        var apiClientId = configuration["AzureAd:ClientId"];

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(apiClientId))
        {
            throw new InvalidOperationException("AzureAd:TenantId and AzureAd:ClientId are required.");
        }

        var applicationIdUri = $"api://{apiClientId}";

        var allowedCallerObjectIds = configuration
            .GetSection(FoundryCallerOptions.SectionName)
            .Get<FoundryCallerOptions>()?
            .AllowedObjectIds
            .Where(objectId => !string.IsNullOrWhiteSpace(objectId))
            .ToArray() ?? [];

        if (allowedCallerObjectIds.Length == 0)
        {
            throw new InvalidOperationException("FoundryCaller:AllowedObjectIds must contain at least one Foundry identity object ID.");
        }

        // Entra ID bearer JWT (machine-to-machine). AllowWebApiToBeAuthorizedByACL (appsettings) admits the
        // app-only Foundry token, which carries no scp/roles claim; the ACL policy below does the authorization.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        // v1 tokens carry the Application ID URI as aud, v2 tokens the client id; both identify this API only.
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidAudiences = [apiClientId, applicationIdUri];
        });

        services.AddAuthorization(options =>
        {
            // Everything requires an authenticated caller unless it explicitly opts out (health, Agent Card).
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            // Authenticated but not an allowed Foundry identity => 403.
            options.AddPolicy(FoundryCallerAuthorization.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    var isAuthorized = FoundryCallerAuthorization.IsAuthorized(context.User, tenantId, allowedCallerObjectIds);

                    if (!isAuthorized && context.Resource is HttpContext httpContext)
                    {
                        httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(typeof(FoundryCallerAuthorization).FullName!)
                            .LogWarning(
                                "Rejected A2A caller: tid {TenantId}, oid {ObjectId}, appid {AppId}. Add the oid to FoundryCaller:AllowedObjectIds if this caller is expected.",
                                context.User.FindFirst("tid")?.Value ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value,
                                context.User.FindFirst("oid")?.Value ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value,
                                context.User.FindFirst("appid")?.Value ?? context.User.FindFirst("azp")?.Value);
                    }

                    return isAuthorized;
                });
            });
        });

        services.AddOptions<A2AHostingOptions>()
            .Bind(configuration.GetSection(A2AHostingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Application (agent composition). Chains AddInfrastructure — never call it again here.
        services.AddApplication(configuration);

        // A2A: the hosting bridge translates A2A requests into runs of this keyed AIAgent.
        services.AddKeyedSingleton<AIAgent>(AgentKey, (provider, _) =>
            provider.GetRequiredService<AzureExploreAgentRuntime>().Agent);
        services.AddA2AServer(AgentKey);

        // Behind an ingress/proxy that terminates TLS, so X-Forwarded-Proto must be honored before auth.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }

    public static async Task InitializeAzureExploreAgentAsync(this WebApplication app)
    {
        var runtime = app.Services.GetRequiredService<AzureExploreAgentRuntime>();

        await runtime.InitializeAsync(app.Lifetime.ApplicationStopping);
    }

    public static void ConfigureApplication(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders();
            app.UseHsts();
        }

        app.Use(LogA2ARequestAsync);

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous();

        // TEMP-DEBUG
        app.MapGet("/debug/run", async (IServiceProvider sp, string q) =>
        {
            var agent = sp.GetRequiredKeyedService<AIAgent>(AgentKey);
            var response = await agent.RunAsync(q);
            return Results.Text(System.Text.Json.JsonSerializer.Serialize(
                response.Messages.Select(m => new { m.Role, Contents = m.Contents.Select(c => c.GetType().Name + ":" + (c as Microsoft.Extensions.AI.TextContent)?.Text).ToArray() }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "application/json");
        }).AllowAnonymous();

        var hosting = app.Services.GetRequiredService<IOptions<A2AHostingOptions>>().Value;
        var agentUrl = new Uri(new Uri(hosting.PublicBaseUrl), A2APath).ToString();

        // Public on purpose: Foundry discovers the remote Agent Card without credentials.
        app.MapWellKnownAgentCard(new AgentCard
        {
            Name = AzureExploreAgentFactory.AgentName,
            Description = "Read-only Azure exploration agent. Inspects the real Azure environment through Azure CLI " +
                          "and correlates it with official Microsoft Learn documentation.",
            Version = "1.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            SupportedInterfaces =
            [
                // Foundry's A2A v1.0 client only supports the JSON-RPC binding, so it is listed first.
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = ProtocolBindingNames.JsonRpc,
                    ProtocolVersion = "1.0",
                },
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = ProtocolBindingNames.HttpJson,
                    ProtocolVersion = "1.0",
                },
            ],
        }).AllowAnonymous();

        app.MapA2AJsonRpc(AgentKey, A2APath)
            .RequireAuthorization(FoundryCallerAuthorization.PolicyName);

        app.MapA2AHttpJson(AgentKey, A2APath)
            .RequireAuthorization(FoundryCallerAuthorization.PolicyName);
    }

    // Logs the raw A2A JSON-RPC exchange and the HTTP status (401/403 show up here when auth rejects the caller).
    private static async Task LogA2ARequestAsync(HttpContext context, Func<Task> next)
    {
        if (!context.Request.Path.StartsWithSegments(A2APath))
        {
            await next();
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AzureExploreAgent.A2A");

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        logger.LogInformation(
            "[a2a] {Method} {Path} authHeader={HasAuth} body: {Body}",
            context.Request.Method,
            context.Request.Path,
            context.Request.Headers.ContainsKey("Authorization"),
            body.Length <= 3_000 ? body : body[..3_000] + "...");

        await next();

        logger.LogInformation(
            "[a2a] responded {StatusCode} (authenticated={IsAuthenticated})",
            context.Response.StatusCode,
            context.User.Identity?.IsAuthenticated == true);
    }
}
