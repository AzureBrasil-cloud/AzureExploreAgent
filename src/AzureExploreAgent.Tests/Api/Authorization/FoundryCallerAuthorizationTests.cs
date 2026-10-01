using System.Security.Claims;
using AzureExploreAgent.Api.Authorization;

namespace AzureExploreAgent.Tests.Api.Authorization;

public class FoundryCallerAuthorizationTests
{
    private const string ExpectedTenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "99999999-9999-9999-9999-999999999999";
    private const string AllowedObjectId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string SecondAllowedObjectId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string UnknownObjectId = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    private static readonly string[] AllowedObjectIds = [AllowedObjectId];

    [Fact]
    public void Allows_expected_tenant_and_allowed_object_id()
    {
        Assert.True(FoundryCallerAuthorization.IsAuthorized(ExpectedTenant, AllowedObjectId, ExpectedTenant, AllowedObjectIds));
    }

    [Fact]
    public void Denies_wrong_tenant()
    {
        Assert.False(FoundryCallerAuthorization.IsAuthorized(OtherTenant, AllowedObjectId, ExpectedTenant, AllowedObjectIds));
    }

    [Fact]
    public void Denies_unknown_object_id()
    {
        Assert.False(FoundryCallerAuthorization.IsAuthorized(ExpectedTenant, UnknownObjectId, ExpectedTenant, AllowedObjectIds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Denies_missing_object_id(string? objectId)
    {
        Assert.False(FoundryCallerAuthorization.IsAuthorized(ExpectedTenant, objectId, ExpectedTenant, AllowedObjectIds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Denies_missing_tenant(string? tenantId)
    {
        Assert.False(FoundryCallerAuthorization.IsAuthorized(tenantId, AllowedObjectId, ExpectedTenant, AllowedObjectIds));
    }

    [Fact]
    public void Denies_everyone_when_no_object_ids_are_allowed()
    {
        Assert.False(FoundryCallerAuthorization.IsAuthorized(ExpectedTenant, AllowedObjectId, ExpectedTenant, []));
    }

    [Theory]
    [InlineData(AllowedObjectId)]
    [InlineData(SecondAllowedObjectId)]
    public void Supports_multiple_allowed_object_ids(string objectId)
    {
        string[] allowed = [AllowedObjectId, SecondAllowedObjectId];

        Assert.True(FoundryCallerAuthorization.IsAuthorized(ExpectedTenant, objectId, ExpectedTenant, allowed));
    }

    [Fact]
    public void Compares_identifiers_case_insensitively()
    {
        Assert.True(FoundryCallerAuthorization.IsAuthorized(
            ExpectedTenant.ToUpperInvariant(), AllowedObjectId.ToUpperInvariant(), ExpectedTenant, AllowedObjectIds));
    }

    [Fact]
    public void Reads_short_tid_and_oid_claims()
    {
        var caller = CreatePrincipal(("tid", ExpectedTenant), ("oid", AllowedObjectId));

        Assert.True(FoundryCallerAuthorization.IsAuthorized(caller, ExpectedTenant, AllowedObjectIds));
    }

    [Fact]
    public void Reads_mapped_tenant_and_object_id_claims()
    {
        var caller = CreatePrincipal(
            ("http://schemas.microsoft.com/identity/claims/tenantid", ExpectedTenant),
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", AllowedObjectId));

        Assert.True(FoundryCallerAuthorization.IsAuthorized(caller, ExpectedTenant, AllowedObjectIds));
    }

    [Fact]
    public void Denies_authenticated_principal_without_identity_claims()
    {
        var caller = CreatePrincipal(("name", "someone"));

        Assert.False(FoundryCallerAuthorization.IsAuthorized(caller, ExpectedTenant, AllowedObjectIds));
    }

    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));
}
