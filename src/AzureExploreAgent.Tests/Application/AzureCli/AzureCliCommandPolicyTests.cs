using AzureExploreAgent.Application.AzureCli;

namespace AzureExploreAgent.Tests.Application.AzureCli;

public class AzureCliCommandPolicyTests
{
    [Theory]
    [InlineData("resource", "list")]
    [InlineData("resource", "show")]
    [InlineData("webapp", "list")]
    [InlineData("network vnet", "list")]
    [InlineData("network private-endpoint", "list")]
    [InlineData("network private-endpoint-connection", "list")]
    [InlineData("group", "list")]
    [InlineData("containerapp", "list")]
    [InlineData("storage account", "list")]
    [InlineData("storage account", "check-name")]
    [InlineData("aks", "get-versions")]
    [InlineData("graph", "query")]
    [InlineData("account", "show")]
    [InlineData("role assignment", "list")]
    public void Allows_read_only_commands(string commandGroup, string operation)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, null);

        Assert.True(decision.IsAllowed, decision.Reason);
    }

    [Theory]
    [InlineData("resource", "delete")]
    [InlineData("group", "delete")]
    [InlineData("webapp", "restart")]
    [InlineData("role assignment", "create")]
    [InlineData("containerapp", "update")]
    [InlineData("account", "set")]
    [InlineData("webapp", "stop")]
    [InlineData("containerapp", "start")]
    [InlineData("aks", "scale")]
    [InlineData("keyvault", "purge")]
    [InlineData("resource", "invoke-action")]
    [InlineData("vm", "run-command")]
    [InlineData("group delete", "show")]
    public void Blocks_write_commands(string commandGroup, string operation)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, null);

        Assert.False(decision.IsAllowed);
        Assert.Empty(decision.Arguments);
    }

    [Theory]
    [InlineData("account", "get-access-token")]
    [InlineData("storage account keys", "list")]
    [InlineData("cosmosdb", "list-keys")]
    [InlineData("keyvault secret", "show")]
    [InlineData("acr credential", "show")]
    [InlineData("aks", "get-credentials")]
    [InlineData("storage account", "show-connection-string")]
    [InlineData("webapp config connection-string", "list")]
    [InlineData("webapp config appsettings", "list")]
    [InlineData("webapp deployment", "list-publishing-credentials")]
    [InlineData("containerapp secret", "list")]
    public void Blocks_commands_that_read_secrets(string commandGroup, string operation)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, null);

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("login", "show")]
    [InlineData("logout", "show")]
    [InlineData("rest", "show")]
    [InlineData("config", "get")]
    [InlineData("extension", "list")]
    [InlineData("ad user", "list")]
    [InlineData("az resource", "list")]
    public void Blocks_command_groups_outside_the_agent_scope(string commandGroup, string operation)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, null);

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("resource list ; env", "list")]
    [InlineData("resource", "list && whoami")]
    [InlineData("resource", "list | cat")]
    [InlineData("resource", "list > file")]
    [InlineData("resource", "list $(env)")]
    [InlineData("resource", "list `env`")]
    public void Blocks_shell_injection_in_command(string commandGroup, string operation)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, null);

        Assert.False(decision.IsAllowed);
        Assert.Equal("Shell syntax is not allowed.", decision.Reason);
    }

    [Theory]
    [InlineData("--resource-group", "rg ; env")]
    [InlineData("--resource-group", "rg && whoami")]
    [InlineData("--resource-group", "rg | cat")]
    [InlineData("--resource-group", "rg > file")]
    [InlineData("--resource-group", "rg $(env)")]
    [InlineData("--name; env", "x")]
    [InlineData("--query", "$(env)")]
    public void Blocks_shell_injection_in_arguments(string name, string value)
    {
        var decision = AzureCliCommandPolicy.Evaluate("resource", "list", new Dictionary<string, string?> { [name] = value });

        Assert.False(decision.IsAllowed);
        Assert.Equal("Shell syntax is not allowed.", decision.Reason);
    }

    [Fact]
    public void Blocks_control_characters_in_argument_values()
    {
        var decision = AzureCliCommandPolicy.Evaluate("resource", "list", new Dictionary<string, string?> { ["--resource-group"] = "rg\nenv" });

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("--resource-group", "@/etc/passwd")]
    [InlineData("--query", "@~/.azure/msal_token_cache.json")]
    public void Blocks_local_file_references(string name, string value)
    {
        var decision = AzureCliCommandPolicy.Evaluate("resource", "list", new Dictionary<string, string?> { [name] = value });

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("-o")]
    [InlineData("--debug")]
    [InlineData("--verbose")]
    [InlineData("--only-show-errors")]
    [InlineData("--password")]
    [InlineData("--account-key")]
    [InlineData("--sas-token")]
    public void Blocks_reserved_and_sensitive_arguments(string name)
    {
        var decision = AzureCliCommandPolicy.Evaluate("resource", "list", new Dictionary<string, string?> { [name] = "value" });

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void Builds_argument_list_with_forced_json_output()
    {
        var decision = AzureCliCommandPolicy.Evaluate(
            "network vnet",
            "list",
            new Dictionary<string, string?> { ["--resource-group"] = "rg-production" });

        Assert.True(decision.IsAllowed);
        Assert.Equal(
            ["network", "vnet", "list", "--resource-group", "rg-production", "--output", "json", "--only-show-errors"],
            decision.Arguments);
    }

    [Fact]
    public void Passes_flags_without_values()
    {
        var decision = AzureCliCommandPolicy.Evaluate("vm", "list", new Dictionary<string, string?> { ["--show-details"] = null });

        Assert.True(decision.IsAllowed);
        Assert.Equal(["vm", "list", "--show-details", "--output", "json", "--only-show-errors"], decision.Arguments);
    }

    [Fact]
    public void Allows_jmespath_operators_in_query()
    {
        const string query = "[?location=='eastus' && type=='Microsoft.Web/sites'].{name:name} | [0]";

        var decision = AzureCliCommandPolicy.Evaluate("resource", "list", new Dictionary<string, string?> { ["--query"] = query });

        Assert.True(decision.IsAllowed, decision.Reason);
        Assert.Contains(query, decision.Arguments);
    }

    [Fact]
    public void Normalizes_casing_and_whitespace()
    {
        var decision = AzureCliCommandPolicy.Evaluate("  Network   VNet ", " LIST ", null);

        Assert.True(decision.IsAllowed);
        Assert.Equal(["network", "vnet", "list", "--output", "json", "--only-show-errors"], decision.Arguments);
    }

    [Theory]
    [InlineData(null, "list")]
    [InlineData("resource", null)]
    [InlineData(" ", "list")]
    [InlineData("resource", "")]
    public void Requires_command_group_and_operation(string? commandGroup, string? operation)
    {
        var decision = AzureCliCommandPolicy.Evaluate(commandGroup, operation, null);

        Assert.False(decision.IsAllowed);
    }
}
