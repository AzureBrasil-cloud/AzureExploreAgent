using AzureExploreAgent.Infrastructure.AzureCli;

namespace AzureExploreAgent.Tests.Infrastructure.AzureCli;

public class AzureCliOutputSanitizerTests
{
    [Fact]
    public void Redacts_known_secrets()
    {
        var result = AzureCliOutputSanitizer.Sanitize("value=my-secret;", "my-secret");

        Assert.Equal($"value={AzureCliOutputSanitizer.Redacted};", result);
    }

    [Fact]
    public void Redacts_bearer_tokens()
    {
        var result = AzureCliOutputSanitizer.Sanitize("Authorization: Bearer abc123.def_456-ghi=");

        Assert.Equal($"Authorization: Bearer {AzureCliOutputSanitizer.Redacted}", result);
    }

    [Fact]
    public void Redacts_jwts()
    {
        var result = AzureCliOutputSanitizer.Sanitize("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl end");

        Assert.Equal($"token {AzureCliOutputSanitizer.Redacted} end", result);
    }

    [Fact]
    public void Leaves_regular_output_untouched()
    {
        const string json = """[{"name":"api-prod"}]""";

        Assert.Equal(json, AzureCliOutputSanitizer.Sanitize(json, "unrelated-secret"));
    }

    [Fact]
    public void Truncates_only_when_over_the_limit()
    {
        Assert.Equal("abc", AzureCliOutputSanitizer.Truncate("abc", 3));
        Assert.Equal("ab" + AzureCliOutputSanitizer.TruncationMarker, AzureCliOutputSanitizer.Truncate("abc", 2));
    }
}
