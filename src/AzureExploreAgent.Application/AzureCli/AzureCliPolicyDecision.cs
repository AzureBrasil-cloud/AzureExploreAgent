namespace AzureExploreAgent.Application.AzureCli;

public sealed record AzureCliPolicyDecision(bool IsAllowed, string? Reason, IReadOnlyList<string> Arguments)
{
    public static AzureCliPolicyDecision Allow(IReadOnlyList<string> arguments) => new(true, null, arguments);

    public static AzureCliPolicyDecision Deny(string reason) => new(false, reason, []);
}
