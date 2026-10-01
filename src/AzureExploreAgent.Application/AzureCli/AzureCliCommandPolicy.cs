using System.Text.RegularExpressions;

namespace AzureExploreAgent.Application.AzureCli;

/// <summary>
/// Read-only command policy for the model-facing Azure CLI tool. Azure RBAC is the real boundary; this policy
/// stops the model from even attempting write operations, secret retrieval or shell tricks. Commands are validated
/// structurally (command group + operation + named arguments) rather than by searching for dangerous words in free text.
/// </summary>
public static partial class AzureCliCommandPolicy
{
    private const int MaxArgumentValueLength = 2_000;
    private const string QueryArgument = "--query";

    private static readonly HashSet<string> AllowedVerbs = ["list", "show", "get", "check", "query"];

    private static readonly HashSet<string> BlockedVerbs =
    [
        "create", "delete", "update", "set", "add", "remove", "assign", "deploy", "restart", "stop", "start",
        "scale", "purge", "restore", "credential", "invoke", "reset", "regenerate", "renew", "rotate", "swap",
        "import", "export", "upload", "download", "enable", "disable", "approve", "reject", "cancel", "move",
    ];

    // Root groups that sign in/out, change CLI state, call arbitrary endpoints or read the directory.
    private static readonly HashSet<string> BlockedCommandGroups =
    [
        "login", "logout", "rest", "config", "extension", "interactive", "upgrade", "cache", "init",
        "feedback", "self-test", "find", "survey", "bicep", "ad",
    ];

    // Words that indicate secret material (keys, tokens, connection strings, publishing profiles, app settings).
    private static readonly HashSet<string> SensitiveWords =
    [
        "secret", "secrets", "credential", "credentials", "password", "passwords", "token", "tokens",
        "key", "keys", "sas", "publishing", "appsettings", "kubeconfig",
    ];

    private static readonly string[] SensitivePhrases = ["connection-string", "publish-profile"];

    // The tool always controls output format and verbosity; --debug would also dump HTTP traffic.
    private static readonly HashSet<string> ReservedArguments =
    [
        "--output", "-o", "--only-show-errors", "--debug", "--verbose", "--help", "-h",
    ];

    private static readonly char[] ShellMetaCharacters = [';', '&', '|', '>', '<', '`', '$'];

    public static AzureCliPolicyDecision Evaluate(
        string? commandGroup,
        string? operation,
        IReadOnlyDictionary<string, string?>? arguments)
    {
        if (string.IsNullOrWhiteSpace(commandGroup) || string.IsNullOrWhiteSpace(operation))
        {
            return AzureCliPolicyDecision.Deny("Both a command group and an operation are required.");
        }

        var groupTokens = commandGroup.Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var normalizedOperation = operation.Trim().ToLowerInvariant();

        foreach (var token in groupTokens.Append(normalizedOperation))
        {
            if (ContainsShellSyntax(token))
            {
                return AzureCliPolicyDecision.Deny("Shell syntax is not allowed.");
            }

            if (!CommandTokenRegex().IsMatch(token))
            {
                return AzureCliPolicyDecision.Deny($"'{token}' is not a valid Azure CLI command token.");
            }
        }

        if (groupTokens[0] == "az")
        {
            return AzureCliPolicyDecision.Deny("Do not include the leading 'az' executable.");
        }

        if (BlockedCommandGroups.Contains(groupTokens[0]))
        {
            return AzureCliPolicyDecision.Deny($"The 'az {groupTokens[0]}' command group is not available to this agent.");
        }

        if (groupTokens.Append(normalizedOperation).Any(IsSensitiveToken))
        {
            return AzureCliPolicyDecision.Deny("Commands that read secrets, keys, tokens or credentials are not allowed.");
        }

        var blockedVerb = groupTokens.Append(normalizedOperation)
            .SelectMany(token => token.Split('-'))
            .FirstOrDefault(BlockedVerbs.Contains);

        if (blockedVerb is not null)
        {
            return AzureCliPolicyDecision.Deny($"'{blockedVerb}' operations are not allowed; only read-only queries are permitted.");
        }

        if (!AllowedVerbs.Contains(normalizedOperation.Split('-')[0]))
        {
            return AzureCliPolicyDecision.Deny(
                $"Operation '{normalizedOperation}' is not read-only. Allowed operations start with: {string.Join(", ", AllowedVerbs)}.");
        }

        List<string> commandArguments = [.. groupTokens, normalizedOperation];

        foreach (var (rawName, value) in arguments ?? new Dictionary<string, string?>())
        {
            var name = rawName.Trim().ToLowerInvariant();
            var argumentDenial = EvaluateArgument(name, value);

            if (argumentDenial is not null)
            {
                return AzureCliPolicyDecision.Deny(argumentDenial);
            }

            commandArguments.Add(name);

            if (value is not null)
            {
                commandArguments.Add(value);
            }
        }

        commandArguments.AddRange(["--output", "json", "--only-show-errors"]);

        return AzureCliPolicyDecision.Allow(commandArguments);
    }

    private static string? EvaluateArgument(string name, string? value)
    {
        if (ContainsShellSyntax(name))
        {
            return "Shell syntax is not allowed.";
        }

        if (!ArgumentNameRegex().IsMatch(name))
        {
            return $"'{name}' is not a valid Azure CLI argument name.";
        }

        if (ReservedArguments.Contains(name))
        {
            return $"The '{name}' argument is controlled by the tool and cannot be supplied.";
        }

        if (IsSensitiveToken(name.TrimStart('-')))
        {
            return "Arguments that carry secrets, keys, tokens or credentials are not allowed.";
        }

        if (value is null)
        {
            return null;
        }

        if (value.Length > MaxArgumentValueLength)
        {
            return $"The value of '{name}' is too long.";
        }

        if (value.Any(char.IsControl))
        {
            return $"The value of '{name}' contains control characters.";
        }

        // Azure CLI loads "@path" values from local files, which would let the model read host files.
        if (value.TrimStart().StartsWith('@'))
        {
            return "Argument values that reference local files ('@') are not allowed.";
        }

        // JMESPath legitimately uses |, &&, ||, <, > and backticks; no shell ever parses the value.
        if (name == QueryArgument)
        {
            return value.Contains("$(", StringComparison.Ordinal) ? "Shell syntax is not allowed." : null;
        }

        return ContainsShellSyntax(value) ? "Shell syntax is not allowed." : null;
    }

    private static bool ContainsShellSyntax(string value) => value.IndexOfAny(ShellMetaCharacters) >= 0;

    private static bool IsSensitiveToken(string token) =>
        token.Split('-').Any(SensitiveWords.Contains)
        || SensitivePhrases.Any(phrase => token.Contains(phrase, StringComparison.Ordinal));

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex CommandTokenRegex();

    [GeneratedRegex("^(--[a-z0-9][a-z0-9-]*|-[a-z])$")]
    private static partial Regex ArgumentNameRegex();
}
