namespace AzureExploreAgent.Api.Authorization;

/// <summary>
/// Binds to the "FoundryCaller" configuration section. Object IDs (<c>oid</c>) of the Foundry identities allowed to
/// execute the agent — the Project Managed Identity or the Agent Identity selected on the Foundry A2A connection.
/// </summary>
public sealed class FoundryCallerOptions
{
    public const string SectionName = "FoundryCaller";

    public string[] AllowedObjectIds { get; set; } = [];
}
