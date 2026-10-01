using AzureExploreAgent.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddConfigurations(builder.Configuration);

var app = builder.Build();

await app.InitializeAzureExploreAgentAsync();

app.ConfigureApplication();

app.Run();

public partial class Program { }
