using System.Security.Claims;

namespace AzureExploreAgent.Api.Authorization;

/// <summary>
/// ACL-style authorization for the A2A execution endpoint. A valid Entra token for this API is not enough:
/// the caller must come from the expected tenant (<c>tid</c>) AND be one of the explicitly allowed Foundry
/// identities (<c>oid</c>) — a Foundry Project Managed Identity or a published Agent Identity.
/// </summary>
public static class FoundryCallerAuthorization
{
    public const string PolicyName = "FoundryCallerOnly";

    private const string TenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ObjectIdClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public static bool IsAuthorized(
        ClaimsPrincipal caller,
        string expectedTenantId,
        IReadOnlyCollection<string> allowedCallerObjectIds)
    {
        var tenantId = caller.FindFirstValue("tid") ?? caller.FindFirstValue(TenantIdClaimType);
        var objectId = caller.FindFirstValue("oid") ?? caller.FindFirstValue(ObjectIdClaimType);

        return IsAuthorized(tenantId, objectId, expectedTenantId, allowedCallerObjectIds);
    }

    public static bool IsAuthorized(
        string? tenantId,
        string? objectId,
        string expectedTenantId,
        IReadOnlyCollection<string> allowedCallerObjectIds)
    {
        if (string.IsNullOrWhiteSpace(expectedTenantId)
            || string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        return string.Equals(tenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase)
            && allowedCallerObjectIds.Contains(objectId, StringComparer.OrdinalIgnoreCase);
    }
}
