using System.Text.RegularExpressions;

namespace AzureExploreAgent.Infrastructure.AzureCli;

/// <summary>
/// Last line of defense before Azure CLI output reaches logs or the model: redacts known secrets,
/// bearer tokens and JWTs, and caps the size of the text.
/// </summary>
public static partial class AzureCliOutputSanitizer
{
    public const string Redacted = "[REDACTED]";
    public const string TruncationMarker = "\n[output truncated]";

    public static string Sanitize(string? text, params IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sanitized = text;

        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                sanitized = sanitized.Replace(secret, Redacted, StringComparison.Ordinal);
            }
        }

        sanitized = BearerTokenRegex().Replace(sanitized, $"Bearer {Redacted}");
        sanitized = JwtRegex().Replace(sanitized, Redacted);

        return sanitized;
    }

    public static string Truncate(string text, int maxCharacters) =>
        text.Length <= maxCharacters
            ? text
            : string.Concat(text.AsSpan(0, maxCharacters), TruncationMarker);

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+")]
    private static partial Regex JwtRegex();
}
