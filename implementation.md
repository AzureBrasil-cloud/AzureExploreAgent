Use Atlas project as ref to how to implement the c#, design patterns, layers, and overall architecture. Pay attention to the separation of concerns, dependency injection, and how services interact with each other.

- Implement in  AzureExploreAgent
- consume the microsoft learn mcp in order to confirm the infos
- use the following guidelines:

# AzureExploreAgent — Integration and Authentication Guidelines

## Scope of this document

This document defines the implementation guidelines for the external integrations used by **AzureExploreAgent**.

Do not use this document to define the repository architecture, project structure, dependency injection organization, configuration classes, naming conventions, or detailed class layout. Those concerns are defined elsewhere.

This document should guide the coding agent specifically on:

- Microsoft Entra authentication for incoming A2A requests.
- Authorization of a specific Microsoft Foundry identity.
- A2A hosting through Microsoft Agent Framework.
- Microsoft Foundry model access.
- Application authentication using Client ID and Client Secret.
- Microsoft Learn MCP integration.
- Azure CLI authentication and execution.
- Azure CLI exposure as an Agent Framework tool.
- Read-only Azure command enforcement.
- Tool composition inside Microsoft Agent Framework.
- Unit testing of the critical integration logic.

Snippets in this document are **suggestive implementation references**. The coding agent should adapt namespaces, overloads and APIs to the latest official SDK versions.

---

# 1. Required SDKs

The implementation will likely need packages in these areas:

```bash
dotnet add package Microsoft.Identity.Web

dotnet add package Azure.Identity

dotnet add package Microsoft.Agents.AI.Foundry --prerelease

dotnet add package Microsoft.Agents.AI.Hosting.A2A.AspNetCore --prerelease

dotnet add package ModelContextProtocol

dotnet add package Microsoft.Extensions.AI
```

Some Microsoft Agent Framework packages are currently prerelease. The coding agent must verify current versions against the official documentation instead of pinning versions based only on this document. Microsoft currently documents `Microsoft.Agents.AI.Foundry` for Foundry-backed `AIAgent` instances and `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` for A2A hosting.

---

# 2. Microsoft Entra protected A2A API

AzureExploreAgent must expose an API represented by a Microsoft Entra App Registration.

The application registration must have an Application ID URI similar to:

```text
api://<AZURE_EXPLORE_AGENT_CLIENT_ID>
```

This URI is the audience expected in access tokens sent by Microsoft Foundry.

The authentication design should follow the same pattern used by the protected MCP scenario:

```text
Microsoft Foundry identity
        ↓
Microsoft Entra ID
        ↓
JWT access token
        ↓
AzureExploreAgent
        ↓
Validate:
    aud
    tid
    oid
```

The important security property is that a token being valid is **not enough**.

The application must additionally ensure that the `oid` claim corresponds to the explicitly authorized Foundry identity.

This reproduces the same ACL-style authorization used in the protected MCP reference scenario.

---

# 3. Register Microsoft.Identity.Web

Use Microsoft.Identity.Web to configure JWT Bearer authentication.

Illustrative implementation:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(
        configuration.GetSection("AzureAd"));
```

Microsoft's current ASP.NET Core guidance uses `AddMicrosoftIdentityWebApi` to validate Microsoft Entra bearer tokens.

The corresponding conceptual configuration is:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-id>",
    "ClientId": "<azure-explore-agent-api-client-id>"
  }
}
```

The exact configuration organization is defined elsewhere in the solution.

---

# 4. Explicit audience validation

The API should explicitly accept its Application ID URI as the JWT audience.

Illustrative approach:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var applicationIdUri =
    $"api://{apiClientId}";

services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        options.TokenValidationParameters.ValidAudiences =
        [
            apiClientId,
            applicationIdUri
        ];
    });
```

The expected Foundry connection should normally request:

```text
aud = api://<client-id>
```

Avoid accepting unrelated audiences.

---

# 5. Validate the Foundry caller identity

The authorization layer must validate:

```text
tid
oid
```

The `tid` must correspond to the expected Microsoft Entra tenant.

The `oid` must correspond to one of the allowed Foundry identities.

Illustrative policy logic:

```csharp
using System.Security.Claims;

var tenantId =
    context.User.FindFirstValue("tid");

var objectId =
    context.User.FindFirstValue("oid");

var validTenant =
    string.Equals(
        tenantId,
        expectedTenantId,
        StringComparison.OrdinalIgnoreCase);

var validCaller =
    !string.IsNullOrWhiteSpace(objectId) &&
    allowedCallerObjectIds.Contains(
        objectId,
        StringComparer.OrdinalIgnoreCase);

return validTenant && validCaller;
```

A policy could conceptually look like:

```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy(
        "FoundryCallerOnly",
        policy =>
        {
            policy.RequireAuthenticatedUser();

            policy.RequireAssertion(context =>
            {
                var tid =
                    context.User.FindFirstValue("tid");

                var oid =
                    context.User.FindFirstValue("oid");

                return
                    string.Equals(
                        tid,
                        expectedTenantId,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    !string.IsNullOrWhiteSpace(oid)
                    &&
                    allowedCallerObjectIds.Contains(
                        oid,
                        StringComparer.OrdinalIgnoreCase);
            });
        });
});
```

Do not authorize based only on:

```text
authenticated == true
```

and do not authorize merely because the token belongs to the same tenant.

---

# 6. Protect the A2A execution endpoint

The A2A endpoint should require the Foundry caller authorization policy.

Illustratively:

```csharp
var endpoint =
    app.MapA2AHttpJson(
        "azure-explore-agent",
        "/a2a/azure-explore-agent");

endpoint.RequireAuthorization(
    "FoundryCallerOnly");
```

The coding agent should adapt this to the actual return type of the current A2A ASP.NET Core SDK.

The result should conceptually be:

```text
POST /a2a/azure-explore-agent

No token
    → 401

Valid Entra token, wrong caller
    → 403

Valid Entra token, authorized Foundry oid
    → agent execution
```

---

# 7. Agent Card

Use the Agent Framework/A2A SDK to expose the Agent Card.

Illustrative example:

```csharp
app.MapWellKnownAgentCard(
    new AgentCard
    {
        Name = "AzureExploreAgent",

        Description =
            "Read-only Azure exploration agent.",

        Version = "1.0",

        DefaultInputModes =
        [
            "text"
        ],

        DefaultOutputModes =
        [
            "text"
        ],

        SupportedInterfaces =
        [
            new AgentInterface
            {
                Url =
                    "https://<host>/a2a/azure-explore-agent",

                ProtocolBinding =
                    ProtocolBindingNames.HttpJson,

                ProtocolVersion =
                    "1.0"
            }
        ]
    });
```

The Agent Framework A2A hosting package currently exposes `MapA2AHttpJson`, while the A2A ASP.NET Core package provides `MapWellKnownAgentCard`.

Prefer leaving the Agent Card publicly readable while keeping execution protected.

Foundry normally retrieves the remote Agent Card without authentication. If the Agent Card is also protected, configure Foundry to send credentials during Agent Card discovery using `send_credentials_for_agent_card`.

---

# 8. Register AzureExploreAgent as an A2A server

Use the Microsoft Agent Framework A2A hosting integration instead of manually implementing A2A request parsing.

Conceptual example:

```csharp
builder.Services.AddKeyedSingleton<AIAgent>(
    "azure-explore-agent",
    (_, _) => agent);

builder.AddA2AServer(
    "azure-explore-agent");
```

Then expose it:

```csharp
app.MapA2AHttpJson(
    "azure-explore-agent",
    "/a2a/azure-explore-agent");
```

Microsoft currently documents this bridge as the mechanism that translates incoming A2A requests into execution against an `AIAgent`.

Do not manually deserialize A2A protocol payloads unless the SDK requires custom behavior unavailable in the hosting package.

---

# 9. Configure the calling Foundry agent

The Foundry-side connection should use Microsoft Entra identity authentication.

For a Foundry Project Managed Identity:

```bash
azd ai connection create azure-explore-agent \
  --kind remote-a2a \
  --target https://<azure-explore-agent-host> \
  --auth-type project-managed-identity \
  --audience "api://<AZURE_EXPLORE_AGENT_CLIENT_ID>"
```

If authorization should be specific to a published agent identity instead of the entire project:

```bash
azd ai connection create azure-explore-agent \
  --kind remote-a2a \
  --target https://<azure-explore-agent-host> \
  --auth-type agentic-identity \
  --audience "api://<AZURE_EXPLORE_AGENT_CLIENT_ID>"
```

Foundry currently supports both `project-managed-identity` and `agentic-identity` for remote A2A connections.

The `oid` observed by AzureExploreAgent must correspond to the identity selected here.

---

# 10. AzureExploreAgent service principal

AzureExploreAgent itself authenticates outbound using:

```text
Tenant ID
Client ID
Client Secret
```

Use an explicit credential instead of relying on ambient developer credentials.

Illustrative code:

```csharp
using Azure.Identity;

var credential =
    new ClientSecretCredential(
        tenantId,
        clientId,
        clientSecret);
```

This credential should be reused for SDK operations that support `TokenCredential`.

The LLM must never receive:

```text
tenant secret
client secret
access token
refresh token
Authorization header
```

---

# 11. Connect to Microsoft Foundry using the service principal

Create the project client using the explicit `ClientSecretCredential`.

```csharp
using Azure.AI.Projects;
using Azure.Identity;

var credential =
    new ClientSecretCredential(
        tenantId,
        clientId,
        clientSecret);

var projectClient =
    new AIProjectClient(
        new Uri(foundryProjectEndpoint),
        credential);
```

Then use the Microsoft Agent Framework Foundry provider.

Microsoft currently recommends `AIProjectClient.AsAIAgent(...)` when the application owns the agent definition, tools and instructions. No persistent server-side agent resource needs to be created.

---

# 12. Create the Microsoft Agent Framework agent

The core pattern should resemble:

```csharp
AIAgent agent =
    projectClient.AsAIAgent(
        model: modelDeploymentName,
        name: "AzureExploreAgent",
        instructions: instructions,
        tools: tools);
```

This is a **Responses Agent / direct inference** pattern:

```text
agent definition
instructions
tools
orchestration
```

live inside AzureExploreAgent.

Foundry provides:

```text
model inference
```

Microsoft currently documents this as the recommended starting pattern for code-first Agent Framework applications backed by Foundry.

---

# 13. Foundry RBAC

The AzureExploreAgent service principal must have permission to invoke the configured Foundry model.

The baseline role for this project should be:

```text
Foundry User
```

Current role definition ID:

```text
53ca6127-db72-4b80-b1b0-d745d6d5456d
```

Illustrative assignment:

```bash
az role assignment create \
  --assignee-object-id "<SERVICE-PRINCIPAL-OBJECT-ID>" \
  --assignee-principal-type ServicePrincipal \
  --role "53ca6127-db72-4b80-b1b0-d745d6d5456d" \
  --scope "<FOUNDRY-SCOPE>"
```

Microsoft currently recommends using the role GUID because the Foundry RBAC role names have been undergoing renaming.

Use the smallest supported scope that permits the required model inference.

Do not grant Owner or Contributor merely to make inference work.

If a newer, more granular role becomes sufficient for the exact inference path used by this application, prefer least privilege after confirming it in current Microsoft documentation.

---

# 14. Load the agent instructions externally

The main instructions should come from externally editable content rather than being permanently embedded in the agent implementation.

Conceptually:

```csharp
var instructions =
    await File.ReadAllTextAsync(
        instructionsPath,
        cancellationToken);
```

Then:

```csharp
var agent =
    projectClient.AsAIAgent(
        model: model,
        name: "AzureExploreAgent",
        instructions: instructions,
        tools: tools);
```

The coding agent decides where this file lives according to the repository guidelines defined elsewhere.

The instructions should reinforce:

```text
Use Azure CLI for real environment state.

Use Microsoft Learn for official Microsoft documentation.

Do not fabricate Azure resources.

Prefer read-only investigation.

Never expose credentials.

Clearly distinguish observed data from recommendations.

Return Markdown.
```

---

# 15. Microsoft Learn MCP

AzureExploreAgent must connect to the official Microsoft Learn MCP endpoint using the official MCP C# SDK.

Microsoft Learn currently exposes a remote MCP server over Streamable HTTP.

Conceptual endpoint:

```text
Microsoft Learn MCP
        ↓
Streamable HTTP
        ↓
ModelContextProtocol C# SDK
        ↓
AITool collection
        ↓
Microsoft Agent Framework
```

---

# 16. Connect to Microsoft Learn MCP using Streamable HTTP

Use `HttpClientTransport`.

Suggested implementation:

```csharp
using ModelContextProtocol.Client;

var transport =
    new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint =
                new Uri(
                    "https://learn.microsoft.com/api/mcp"),

            TransportMode =
                HttpTransportMode.StreamableHttp,

            ConnectionTimeout =
                TimeSpan.FromSeconds(30)
        });
```

Then create the MCP client:

```csharp
await using var mcpClient =
    await McpClient.CreateAsync(
        transport,
        cancellationToken: cancellationToken);
```

The official MCP C# SDK recommends Streamable HTTP for remote MCP servers and exposes `HttpClientTransport` for this purpose.

---

# 17. Discover Microsoft Learn tools dynamically

Do not hardcode Learn MCP tool names.

Retrieve them from the MCP server:

```csharp
var learnTools =
    await mcpClient.ListToolsAsync(
        cancellationToken: cancellationToken);
```

Microsoft Agent Framework's .NET MCP integration currently supports taking the returned MCP tools and exposing them directly as `AITool` instances.

Conceptually:

```csharp
AITool[] learnAgentTools =
[
    .. learnTools.Cast<AITool>()
];
```

The important behavior is:

```text
MCP server decides available tools
        ↓
application discovers tools
        ↓
Agent Framework receives tools
```

Do not couple AzureExploreAgent to a fixed list of Microsoft Learn MCP functions.

---

# 18. MCP client lifetime

Do not create the MCP client, retrieve tools and immediately dispose the client while the agent continues using those tools.

The underlying tool objects may need the MCP connection to remain alive.

The coding agent must ensure that:

```text
MCP client lifetime
    >=
agent/tool usage lifetime
```

Dispose the client cleanly when the owning runtime shuts down.

Use asynchronous disposal:

```csharp
await mcpClient.DisposeAsync();
```

or appropriate `await using` semantics when lifetime boundaries allow it.

---

# 19. Combine Learn tools with other Agent Framework tools

The agent's tool set should combine:

```text
Microsoft Learn MCP tools
Azure CLI function tool
```

Illustratively:

```csharp
var tools =
    new List<AITool>();

tools.AddRange(
    learnTools.Cast<AITool>());

tools.Add(
    azureCliTool);
```

Then:

```csharp
var agent =
    projectClient.AsAIAgent(
        model: modelDeployment,
        name: "AzureExploreAgent",
        instructions: instructions,
        tools: tools);
```

Foundry Responses Agents created through `AsAIAgent` support standard Agent Framework tools, including function tools and local MCP tools.

---

# 20. Azure CLI operating model

AzureExploreAgent must execute the real Azure CLI available in its runtime environment.

Required runtime assumption:

```text
az executable is installed and available
```

Do not emulate Azure CLI calls.

Do not have the LLM produce fake Azure data.

Actual resource state must come from:

```text
az ...
```

executed against the service principal's authorized Azure scope.

---

# 21. Authenticate Azure CLI as a service principal

Azure CLI must authenticate using the same AzureExploreAgent application identity:

```text
Client ID
Client Secret
Tenant ID
```

Equivalent command:

```bash
az login \
  --service-principal \
  --username "<CLIENT-ID>" \
  --password "<CLIENT-SECRET>" \
  --tenant "<TENANT-ID>"
```

This is the currently documented Azure CLI service principal authentication flow.

The LLM must not generate or execute this login command.

Authentication belongs to application infrastructure.

---

# 22. Execute Azure CLI authentication programmatically

The application may invoke `az` as a child process.

Conceptual C#:

```csharp
var startInfo =
    new ProcessStartInfo
    {
        FileName = "az",

        RedirectStandardOutput = true,
        RedirectStandardError = true,

        UseShellExecute = false,
        CreateNoWindow = true
    };

startInfo.ArgumentList.Add("login");
startInfo.ArgumentList.Add("--service-principal");

startInfo.ArgumentList.Add("--username");
startInfo.ArgumentList.Add(clientId);

startInfo.ArgumentList.Add("--password");
startInfo.ArgumentList.Add(clientSecret);

startInfo.ArgumentList.Add("--tenant");
startInfo.ArgumentList.Add(tenantId);

startInfo.ArgumentList.Add("--output");
startInfo.ArgumentList.Add("none");
```

Then:

```csharp
using var process =
    Process.Start(startInfo)
    ?? throw new InvalidOperationException(
        "Unable to start Azure CLI.");

await process.WaitForExitAsync(
    cancellationToken);

if (process.ExitCode != 0)
{
    var error =
        await process.StandardError.ReadToEndAsync(
            cancellationToken);

    throw new InvalidOperationException(
        $"Azure CLI authentication failed: {error}");
}
```

Never log the complete process argument list during login because it contains the client secret.

---

# 23. Isolate the Azure CLI authentication context

Prefer giving the application its own Azure CLI configuration directory instead of sharing an interactive developer's CLI cache.

For example:

```csharp
startInfo.Environment["AZURE_CONFIG_DIR"] =
    isolatedAzureCliDirectory;
```

Use the same isolated `AZURE_CONFIG_DIR` for subsequent Azure CLI operations.

The objective is:

```text
AzureExploreAgent CLI session
        !=
developer personal CLI session
```

This also makes containerized deployment behavior deterministic.

---

# 24. Select the configured Azure subscription

After login, select the configured subscription:

```bash
az account set \
  --subscription "<SUBSCRIPTION-ID>"
```

Conceptually from C#:

```csharp
ArgumentList.Add("account");
ArgumentList.Add("set");

ArgumentList.Add("--subscription");
ArgumentList.Add(subscriptionId);
```

Then optionally verify:

```bash
az account show --output json
```

The result can be used to validate that the runtime is using the intended:

```text
tenant
subscription
service principal
```

---

# 25. Azure RBAC defines the real scope

The service principal must receive a read-oriented Azure RBAC role.

Default recommendation:

```text
Reader
```

at the smallest required scope.

Examples:

```text
single resource

resource group

subscription
```

Illustrative assignment:

```bash
az role assignment create \
  --assignee-object-id "<SERVICE-PRINCIPAL-OBJECT-ID>" \
  --assignee-principal-type ServicePrincipal \
  --role "Reader" \
  --scope "<TARGET-AZURE-SCOPE>"
```

Azure RBAC role assignments consist of a principal, role and scope, and Microsoft recommends granting permissions at the appropriate scope.

Do not simulate scope protection using prompts.

The real boundary is Azure RBAC.

---

# 26. Expose Azure CLI as a function tool

Do not expose the entire operating-system shell to the LLM by default.

Instead, create a function tool specifically for Azure CLI queries.

Conceptually:

```csharp
[Description(
    "Executes a read-only Azure CLI query against the configured Azure environment and returns the real command output.")]
async Task<string> ExecuteAzureCliAsync(
    [Description(
        "Azure CLI arguments excluding the initial 'az' executable.")]
    string command,
    CancellationToken cancellationToken)
{
    // Validate command.
    // Execute az.
    // Capture JSON.
    // Return result.
}
```

Convert it to an Agent Framework function tool:

```csharp
using Microsoft.Extensions.AI;

var azureCliTool =
    AIFunctionFactory.Create(
        ExecuteAzureCliAsync,
        name: "execute_azure_cli",
        description:
            "Execute a read-only Azure CLI query against the authorized Azure scope.");
```

`AIFunctionFactory.Create` is the standard Agent Framework / Microsoft.Extensions.AI mechanism for turning .NET methods into model-callable function tools.

---

# 27. Prefer structured tool arguments over arbitrary shell text

An even safer interface is to avoid receiving a complete shell command.

Prefer something conceptually like:

```csharp
ExecuteAzureCliAsync(
    string commandGroup,
    string operation,
    IReadOnlyDictionary<string, string?> arguments,
    CancellationToken cancellationToken)
```

Example model request:

```json
{
  "commandGroup": "webapp",
  "operation": "list",
  "arguments": {
    "--resource-group": "rg-production"
  }
}
```

The application constructs:

```text
az webapp list --resource-group rg-production --output json
```

This is safer than allowing:

```text
az webapp list && cat /etc/passwd
```

---

# 28. Never invoke Azure CLI through a shell when unnecessary

Prefer:

```csharp
ProcessStartInfo.FileName = "az";
```

with:

```csharp
ProcessStartInfo.ArgumentList
```

instead of:

```text
bash -c "az ..."
```

or:

```text
powershell -Command "az ..."
```

Using `ArgumentList` avoids normal shell interpretation of characters such as:

```text
;
&&
||
|
>
<
$()
`
```

Example:

```csharp
var psi =
    new ProcessStartInfo
    {
        FileName = "az",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

psi.ArgumentList.Add("network");
psi.ArgumentList.Add("vnet");
psi.ArgumentList.Add("list");

psi.ArgumentList.Add("--output");
psi.ArgumentList.Add("json");

psi.ArgumentList.Add("--only-show-errors");
```

---

# 29. Execute and capture Azure CLI output

Suggested execution pattern:

```csharp
using var process =
    new Process
    {
        StartInfo = psi
    };

process.Start();

var stdoutTask =
    process.StandardOutput.ReadToEndAsync(
        cancellationToken);

var stderrTask =
    process.StandardError.ReadToEndAsync(
        cancellationToken);

await process.WaitForExitAsync(
    cancellationToken);

var stdout =
    await stdoutTask;

var stderr =
    await stderrTask;

if (process.ExitCode != 0)
{
    // Sanitize before exposing this to the model.
}
```

Return something structured internally:

```csharp
public sealed record AzureCliResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
```

The actual naming is an implementation choice.

---

# 30. Force JSON where possible

For queries intended for model analysis, append:

```text
--output json
```

and preferably:

```text
--only-show-errors
```

Example:

```text
az resource list --output json --only-show-errors
```

Structured data should be preferred over table-formatted terminal output.

The agent can reason much more reliably over:

```json
[
  {
    "name": "api-prod",
    "type": "Microsoft.Web/sites"
  }
]
```

than over terminal-aligned text.

---

# 31. Implement a read-only command policy

Azure RBAC is the ultimate authorization boundary, but AzureExploreAgent should also prevent the model from intentionally invoking write commands.

Typical allowed verbs:

```text
list
show
get
check
query
```

Examples:

```text
az resource list

az resource show

az group list

az webapp list

az webapp show

az network vnet list

az network private-endpoint list

az containerapp list

az storage account list
```

Typical blocked verbs:

```text
create
delete
update
set
add
remove
assign
deploy
restart
stop
start
scale
purge
restore
credential
```

Examples that should be rejected:

```text
az group delete

az webapp restart

az role assignment create

az resource delete

az containerapp update
```

Do not rely only on a text search for dangerous words.

Prefer explicit command-group + operation validation where practical.

---

# 32. Block command chaining and shell injection

If a raw command string is accepted anywhere, reject shell syntax such as:

```text
;
&&
||
|
>
>>
<
$(
`
```

Examples that must never execute:

```text
resource list ; rm -rf /

resource list && env

webapp list | curl ...

resource list > /tmp/secrets
```

Better still, never pass raw command strings into a shell.

---

# 33. Azure CLI timeout

Every Azure CLI process must have a timeout.

Conceptually:

```csharp
using var timeout =
    CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken);

timeout.CancelAfter(
    TimeSpan.FromSeconds(30));

await process.WaitForExitAsync(
    timeout.Token);
```

On timeout:

```text
terminate process
terminate child processes when practical
return controlled failure
```

Do not allow indefinitely running tool calls.

---

# 34. Azure CLI output limits

Do not send unlimited CLI output to the model.

Before returning command output:

```csharp
if (stdout.Length > maxCharacters)
{
    stdout =
        stdout[..maxCharacters]
        + "\n[output truncated]";
}
```

For potentially huge resource sets, encourage the model to narrow the query using Azure CLI filtering rather than requesting the entire subscription repeatedly.

---

# 35. Azure CLI errors exposed to the model

The model should receive sanitized errors.

Example:

```text
Azure CLI command failed.

Exit code: 1

Azure error:
AuthorizationFailed

The configured service principal does not have permission
to access the requested scope.
```

Do not include:

```text
tokens
secrets
Authorization headers
environment variables
full login commands
```

---

# 36. Do not expose environment variables to the agent

The Azure CLI tool must not permit commands like:

```text
env
printenv
set
Get-ChildItem Env:
```

The agent does not need access to the host environment.

The only exposed capability should be controlled Azure CLI querying.

---

# 37. Agent Framework shell tooling

Microsoft Agent Framework currently provides shell support through tooling such as `LocalShellExecutor`.

Example from the current API shape:

```csharp
await using var shell =
    new LocalShellExecutor(
        new()
        {
            Mode =
                ShellMode.Stateless,

            AcknowledgeUnsafe =
                true
        });

var shellTool =
    shell.AsAIFunction(
        requireApproval: false);
```

However, do **not** use unrestricted `LocalShellExecutor` as AzureExploreAgent's default production tool.

Microsoft explicitly warns that the host shell is not a sandbox and can access files, processes, credentials and external systems.

Use a dedicated Azure CLI wrapper instead.

The shell SDK may still be useful as a reference implementation.

---

# 38. Complete tool composition example

Conceptually, the final tool initialization may resemble:

```csharp
var learnTransport =
    new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint =
                new Uri(
                    "https://learn.microsoft.com/api/mcp"),

            TransportMode =
                HttpTransportMode.StreamableHttp
        });

var learnClient =
    await McpClient.CreateAsync(
        learnTransport,
        cancellationToken: cancellationToken);

var learnTools =
    await learnClient.ListToolsAsync(
        cancellationToken: cancellationToken);

var azureCliTool =
    AIFunctionFactory.Create(
        ExecuteAzureCliAsync,
        name: "execute_azure_cli",
        description:
            "Executes safe read-only Azure CLI queries against the configured Azure environment.");

var agentTools =
    new List<AITool>();

agentTools.AddRange(
    learnTools.Cast<AITool>());

agentTools.Add(
    azureCliTool);
```

Then:

```csharp
var credential =
    new ClientSecretCredential(
        tenantId,
        clientId,
        clientSecret);

var projectClient =
    new AIProjectClient(
        new Uri(foundryProjectEndpoint),
        credential);

var agent =
    projectClient.AsAIAgent(
        model: modelDeployment,
        name: "AzureExploreAgent",
        instructions: instructions,
        tools: agentTools);
```

This is the fundamental runtime composition:

```text
Foundry model
     +
Microsoft Learn MCP tools
     +
Azure CLI function tool
     =
AzureExploreAgent
```

---

# 39. Expected agent decision flow

The system instructions should encourage the following behavior:

```text
User asks conceptual Azure question
        ↓
Use model knowledge
        ↓
Use Microsoft Learn if current documentation matters
```

```text
User asks about the actual Azure environment
        ↓
Use Azure CLI
```

```text
User asks why the actual environment behaves a certain way
        ↓
Azure CLI
        +
Microsoft Learn
        ↓
compare observed state with documented behavior
```

Example:

```text
Why can't app-prod reach Foundry through the private endpoint?
```

Expected tool flow:

```text
1. Inspect App Service.

2. Inspect VNet Integration.

3. Inspect subnet.

4. Inspect private endpoints.

5. Inspect private DNS information accessible to the identity.

6. Consult Microsoft Learn for expected networking behavior.

7. Correlate documentation and actual configuration.

8. Return Markdown.
```

---

# 40. Natural-language Markdown result

The final A2A result should be natural-language Markdown.

Example:

```markdown
## Observed configuration

The App Service `api-prod` is integrated with `snet-appservice`.

A private endpoint exists for the Foundry resource.

## Finding

I could not find the expected private DNS zone association within
the Azure scope available to this agent.

## Microsoft guidance

Private endpoint name resolution depends on the corresponding
private DNS configuration being reachable from the workload VNet.

## Suggested verification

Check whether the VNet used by `api-prod` is linked to the expected
private DNS zone.
```

The answer should distinguish:

```text
observed environment state
```

from:

```text
general guidance
```

---

# 41. Unit tests only

Implement **unit tests only**.

Do not create integration tests.

Unit tests must not require:

```text
real Azure credentials
real Azure subscription
real Foundry project
real Microsoft Learn MCP
real Azure CLI login
real A2A caller
```

---

# 42. Unit test authentication logic

Unit test the Foundry caller ACL behavior.

Examples:

```csharp
Assert.True(
    IsAuthorized(
        expectedTenant,
        allowedObjectId));

Assert.False(
    IsAuthorized(
        otherTenant,
        allowedObjectId));

Assert.False(
    IsAuthorized(
        expectedTenant,
        unknownObjectId));

Assert.False(
    IsAuthorized(
        expectedTenant,
        null));
```

Test:

```text
correct tid + correct oid → allowed

wrong tid → denied

unknown oid → denied

missing oid → denied

multiple allowed oids → supported
```

---

# 43. Unit test Azure CLI policy

Test safe commands:

```text
resource list
resource show
webapp list
network vnet list
network private-endpoint list
```

Test blocked commands:

```text
resource delete
group delete
webapp restart
role assignment create
containerapp update
```

Test shell-injection attempts:

```text
resource list ; env

resource list && whoami

resource list | cat

resource list > file

resource list $(env)
```

These tests are critical.

---

# 44. Unit test process execution abstraction

Do not execute the real `az` binary during unit tests.

Wrap process execution behind an abstraction that can simulate:

```text
exit code 0

exit code 1

stdout JSON

stderr

timeout

cancellation

large output
```

Example expected behavior:

```text
Azure CLI returns exit code 0
    → tool returns stdout

Azure CLI returns AuthorizationFailed
    → controlled error returned

Azure CLI times out
    → execution cancelled

Azure CLI produces excessive output
    → output truncated
```

---

# 45. Unit test Microsoft Learn integration

Mock the MCP client boundary.

Test:

```text
tools discovered successfully

empty tool collection

MCP connection failure

tool discovery failure

returned tools included in agent tool collection
```

Do not contact Microsoft Learn during unit tests.

---

# 46. Unit test agent composition

Verify that the agent receives:

```text
configured Foundry model

instructions

Microsoft Learn MCP tools

Azure CLI function tool
```

Do not test LLM response quality.

Test deterministic composition behavior only.

---

# 47. Security invariants

The implementation must preserve all of these properties:

```text
Inbound A2A requires Entra authentication.

The expected audience is validated.

The expected tenant is validated.

The caller Object ID is validated.

Only configured Foundry identities can execute the agent.

The application Client Secret never reaches the LLM.

Azure CLI login is performed by infrastructure.

The LLM cannot invoke az login.

The LLM cannot access host environment variables.

The LLM cannot execute arbitrary shell commands.

Azure CLI commands are restricted.

Azure RBAC defines the actual Azure visibility boundary.

Foundry access is granted separately through Foundry RBAC.

Microsoft Learn is accessed through MCP.

Real tenant state comes from Azure CLI.

Final responses are Markdown.
```

---

# 48. Final implementation concept

The resulting behavior should be:

```text
Foundry Agent
      │
      │ Project Managed Identity
      │ or Agent Identity
      │
      ▼
Microsoft Entra
      │
      │ Bearer JWT
      ▼
AzureExploreAgent A2A endpoint
      │
      │ validate aud
      │ validate tid
      │ validate oid
      ▼
Microsoft Agent Framework
      │
      ├───────────────┬─────────────────────┐
      │               │                     │
      ▼               ▼                     ▼
Foundry Model   Microsoft Learn MCP   execute_azure_cli
                                            │
                                            ▼
                                         az CLI
                                            │
                                    service principal
                                            │
                                         Azure RBAC
                                            │
                                            ▼
                                    Authorized Azure scope
```

The guiding principle is:

```text
REASON
    Microsoft Foundry

LEARN
    Microsoft Learn MCP

OBSERVE
    Azure CLI

AUTHENTICATE
    Microsoft Entra ID

AUTHORIZE
    oid ACL + Azure RBAC

INTEROPERATE
    A2A
```



Now I need to create a simpple readme. Use the following info + yuor implementation as a reference.

# AzureExploreAgent — README Guidelines

## Purpose

Create a comprehensive `README.md` for the **AzureExploreAgent** repository.

The README must explain both:

1. What AzureExploreAgent is and how it works.
2. How a developer can configure their Azure environment and Microsoft Foundry project so another Foundry agent can securely call AzureExploreAgent through A2A.

The README should be practical and operational.

A developer who knows Azure but has never seen this repository should be able to follow the README from beginning to end and finish with a working integration.

Do not focus on the internal C# architecture in excessive detail. The README should primarily explain the product, security model, required Azure configuration, Foundry configuration, local/deployment configuration and end-to-end validation.

---

# 1. Repository introduction

Start by explaining that **AzureExploreAgent** is a read-only Azure investigation agent built with:

```text
ASP.NET Core
Microsoft Agent Framework
Microsoft Foundry
Agent-to-Agent protocol
Microsoft Entra ID
Microsoft Learn MCP
Azure CLI
Azure RBAC
```

Its purpose is to allow another agent to ask natural-language questions about an Azure environment.

Example:

```text
"What App Services exist in this Resource Group?"

"Which applications use VNet Integration?"

"Which private endpoints exist in this subscription?"

"Why might this App Service be unable to reach this private Foundry endpoint?"

"Explain the network architecture of this environment."
```

AzureExploreAgent can combine:

```text
real Azure configuration
+
official Microsoft documentation
+
LLM reasoning
```

to produce a Markdown response.

---

# 2. Explain the core capabilities

The README should explain the four main capabilities.

```text
REASON
Microsoft Foundry model

LEARN
Microsoft Learn MCP

OBSERVE
Azure CLI

INTEROPERATE
A2A
```

Explain that AzureExploreAgent itself runs inside the ASP.NET Core application.

It is not necessary to create AzureExploreAgent as a persistent Foundry Agent Service agent.

Microsoft Agent Framework hosts the agent inside the application.

Microsoft Foundry supplies the model used by that agent.

---

# 3. Architecture overview

Include a diagram similar to:

```text
                         Microsoft Foundry
                                │
                         Calling Agent
                                │
                    Project Managed Identity
                      or Agent Identity
                                │
                                ▼
                        Microsoft Entra ID
                                │
                       Bearer Access Token
                                │
                                ▼
                    AzureExploreAgent
                     ASP.NET Core / A2A
                                │
                                ▼
                  Microsoft Agent Framework
                       AzureExploreAgent
                    ┌───────────┼───────────┐
                    │           │           │
                    ▼           ▼           ▼
               Foundry LLM   Learn MCP   Azure CLI
                    │                       │
                    │                       ▼
                    │                  Azure RBAC
                    │                       │
                    │                       ▼
                    │               Authorized Scope
                    │
                    └───────── reasoning ──────────┘
```

Explain that there are **two different authentication flows**.

---

# 4. Explain the two authentication flows

This distinction must be very clear in the README.

## Flow A — Foundry calls AzureExploreAgent

This protects the inbound A2A API.

```text
Foundry Agent
      ↓
Foundry Managed Identity / Agent Identity
      ↓
Microsoft Entra
      ↓
Access Token
      ↓
AzureExploreAgent A2A API
```

AzureExploreAgent validates:

```text
aud
tid
oid
```

Where:

```text
aud
Identifies the AzureExploreAgent API.

tid
Identifies the Microsoft Entra tenant.

oid
Identifies the exact Foundry identity allowed to call the API.
```

This means another application in the same tenant cannot automatically call AzureExploreAgent merely because it has a valid Microsoft Entra token.

The caller must match the explicitly configured Object ID.

Explain that this model is based on the same pattern used to protect an MCP endpoint with Microsoft Entra ID and Foundry Managed Identity.

---

# 5. Flow B — AzureExploreAgent accesses Azure and Foundry

AzureExploreAgent itself has a Microsoft Entra App Registration / service principal.

It uses:

```text
Tenant ID
Client ID
Client Secret
```

for outbound authentication.

Conceptually:

```text
AzureExploreAgent
      │
      │ Client ID
      │ Client Secret
      ▼
Microsoft Entra
      │
      ├──────────────► Microsoft Foundry
      │                    Foundry User
      │
      └──────────────► Azure CLI / ARM
                           Reader
                           configured scope
```

Explain explicitly:

> The Client Secret is not used by Foundry to call the A2A endpoint. It is used by AzureExploreAgent when AzureExploreAgent itself needs to authenticate to Microsoft Foundry and Azure.

---

# 6. Prerequisites

The README should include prerequisites similar to:

```text
Microsoft Azure subscription

Microsoft Entra tenant

Microsoft Foundry resource and project

A deployed model inside Microsoft Foundry

Permission to create an App Registration

Permission to assign Azure RBAC roles

A Foundry agent that will consume AzureExploreAgent through A2A

.NET SDK required by the repository

Azure CLI installed in the AzureExploreAgent runtime

Azure Developer CLI if the documented Foundry A2A CLI setup uses azd
```

Also explain that the machine/container hosting AzureExploreAgent needs network access to:

```text
Microsoft Entra ID

Microsoft Foundry

Azure Resource Manager

Microsoft Learn MCP
```

---

# 7. Step 1 — Create the AzureExploreAgent App Registration

The first configuration step should create a Microsoft Entra App Registration for AzureExploreAgent.

Suggested display name:

```text
AzureExploreAgent
```

After creating it, record:

```text
Application (client) ID

Directory (tenant) ID
```

The README should explain the distinction between:

```text
Application / Client ID

Service Principal Object ID
```

because both values will be needed for different purposes.

---

# 8. Step 2 — Expose AzureExploreAgent as an API

Navigate to:

```text
Microsoft Entra ID
→ App registrations
→ AzureExploreAgent
→ Expose an API
```

Configure the Application ID URI.

Recommended value:

```text
api://<APPLICATION-CLIENT-ID>
```

Explain that this value represents the protected AzureExploreAgent API.

It is also the audience that Foundry requests when obtaining the token used to call AzureExploreAgent.

Example:

```text
Application ID

11111111-2222-3333-4444-555555555555
```

Application ID URI:

```text
api://11111111-2222-3333-4444-555555555555
```

Then Foundry must request:

```text
audience =
api://11111111-2222-3333-4444-555555555555
```

The README should explicitly state that an audience mismatch results in authentication failure before the agent executes.

---

# 9. Step 3 — Create the Client Secret

Navigate to:

```text
Microsoft Entra ID
→ App registrations
→ AzureExploreAgent
→ Certificates & secrets
```

Create a Client Secret.

Record the **secret value immediately**.

Explain that the secret value is only displayed once.

The application will eventually require:

```text
Tenant ID
Client ID
Client Secret
```

Never commit the Client Secret into the repository.

Use environment configuration or an appropriate secure secret mechanism.

---

# 10. Step 4 — Find the Service Principal Object ID

The App Registration creates a corresponding Enterprise Application / Service Principal.

Find its Object ID.

Conceptually:

```text
App Registration

Application ID:
1111...

         ↓

Enterprise Application

Object ID:
aaaa...
```

Explain that Azure RBAC assignments are normally granted to the **Service Principal Object ID**, not to the application registration's Client ID.

The README should warn users not to confuse the two.

---

# 11. Step 5 — Grant AzureExploreAgent access to Microsoft Foundry

AzureExploreAgent needs permission to use the configured Microsoft Foundry project/model.

Grant its service principal:

```text
Foundry User
```

at the appropriate Foundry scope.

Prefer the smallest scope that allows the required model invocation.

Conceptually:

```text
AzureExploreAgent Service Principal
              │
              ▼
         Foundry User
              │
              ▼
        Foundry Project
              │
              ▼
       Model Deployment
```

Explain that this permission enables the Microsoft Agent Framework agent inside AzureExploreAgent to use the configured Foundry model.

Do not instruct users to grant:

```text
Owner
Contributor
```

unless another unrelated requirement explicitly needs those roles.

Use least privilege.

---

# 12. Step 6 — Configure the Foundry model

The person deploying the project must provide:

```text
Foundry Project Endpoint

Model Deployment Name
```

Show the expected conceptual format:

```text
https://<foundry-account>.services.ai.azure.com/api/projects/<project>
```

and:

```text
ModelDeployment:
<deployment-name>
```

Explain that AzureExploreAgent authenticates to that endpoint using:

```text
ClientSecretCredential
```

with the App Registration created earlier.

Conceptually:

```csharp
var credential =
    new ClientSecretCredential(
        tenantId,
        clientId,
        clientSecret);

var projectClient =
    new AIProjectClient(
        new Uri(projectEndpoint),
        credential);
```

The coding implementation may change over time as SDK APIs evolve, but this is the intended authentication model.

---

# 13. Step 7 — Grant Azure read access

Now decide **what AzureExploreAgent should be allowed to inspect**.

The README must emphasize that this decision is made through **Azure RBAC**.

The application should normally receive:

```text
Reader
```

at the desired scope.

Possible scopes:

```text
Individual resource

Resource Group

Subscription
```

Example:

```text
AzureExploreAgent Service Principal
              │
            Reader
              │
              ▼
Resource Group rg-production
```

This means AzureExploreAgent can inspect resources inside `rg-production`.

It should not automatically receive access to unrelated Resource Groups.

Example CLI:

```bash
az role assignment create \
  --assignee-object-id "<AZURE-EXPLORE-SERVICE-PRINCIPAL-OBJECT-ID>" \
  --assignee-principal-type ServicePrincipal \
  --role "Reader" \
  --scope "/subscriptions/<SUBSCRIPTION-ID>/resourceGroups/<RESOURCE-GROUP>"
```

Explain that the scope is deployment-specific.

Someone may intentionally grant Reader to:

```text
one resource
```

while another deployment may grant Reader to:

```text
an entire subscription
```

AzureExploreAgent does not define that boundary.

**Azure RBAC defines it.**

---

# 14. Step 8 — Configure Azure CLI access

AzureExploreAgent uses Azure CLI to inspect the real Azure environment.

Explain that the runtime must contain:

```text
az
```

and that AzureExploreAgent authenticates Azure CLI using its own service principal.

Equivalent authentication flow:

```bash
az login \
  --service-principal \
  --username "<CLIENT-ID>" \
  --password "<CLIENT-SECRET>" \
  --tenant "<TENANT-ID>"
```

Then:

```bash
az account set \
  --subscription "<SUBSCRIPTION-ID>"
```

The README should explicitly explain that **the LLM does not execute `az login`**.

Authentication happens in the application infrastructure.

The LLM only gets access to the controlled Azure CLI tool after authentication has succeeded.

---

# 15. Step 9 — Microsoft Learn MCP

Explain that Microsoft Learn requires no tenant-specific configuration by default.

AzureExploreAgent connects to the official Microsoft Learn MCP server.

Its purpose is to provide current official documentation for:

```text
Azure CLI

Azure networking

App Service

Container Apps

AKS

Microsoft Foundry

Azure RBAC

Microsoft Entra

Azure Monitor

Storage

Key Vault

other Microsoft services
```

Conceptual flow:

```text
AzureExploreAgent
       │
       │ MCP
       ▼
Microsoft Learn
       │
       ▼
Official documentation
```

The README should explain why this is important.

Example:

```text
Question:

"Why can't this App Service access the private endpoint?"
```

AzureExploreAgent can use:

```text
Azure CLI
    → inspect the actual environment

Microsoft Learn
    → retrieve documented Microsoft behavior

Foundry model
    → correlate both
```

---

# 16. Step 10 — Configure AzureExploreAgent

Document all configuration values required to run the application.

The exact configuration mechanism may be documented separately, but the README should explain the meaning of every required value.

At minimum:

```text
AZURE_TENANT_ID

AZURE_CLIENT_ID

AZURE_CLIENT_SECRET

AZURE_SUBSCRIPTION_ID

FOUNDRY_PROJECT_ENDPOINT

FOUNDRY_MODEL_DEPLOYMENT

A2A_AUDIENCE

ALLOWED_FOUNDRY_CALLER_OBJECT_ID
```

If multiple callers are supported:

```text
ALLOWED_FOUNDRY_CALLER_OBJECT_IDS
```

Explain:

```text
AZURE_CLIENT_ID

Identity of AzureExploreAgent.
```

```text
AZURE_CLIENT_SECRET

Credential AzureExploreAgent uses for outbound authentication.
```

```text
A2A_AUDIENCE

Normally api://<AZURE_CLIENT_ID>.
```

```text
ALLOWED_FOUNDRY_CALLER_OBJECT_ID

Object ID of the Foundry identity permitted to invoke AzureExploreAgent.
```

---

# 17. Step 11 — Deploy AzureExploreAgent

Explain that the application can be hosted anywhere capable of running the required .NET runtime and Azure CLI.

Typical Azure hosting options could include:

```text
Azure App Service

Azure Container Apps

AKS

containerized infrastructure
```

Do not make a single hosting technology mandatory unless the repository itself requires it.

The deployed application needs a public or network-reachable HTTPS endpoint accessible from Foundry.

Example:

```text
https://azure-explore-agent.example.com
```

The A2A endpoint then becomes conceptually:

```text
https://azure-explore-agent.example.com/a2a/azure-explore-agent
```

The Agent Card should also be reachable according to the A2A implementation.

---

# 18. Step 12 — Find the Foundry caller identity

This is one of the most important steps and should closely follow the authentication model demonstrated in the reference article.

Determine which Foundry identity will call AzureExploreAgent.

Two main options exist.

## Project Managed Identity

All agents using that project connection share the Foundry project's managed identity.

Retrieve its Object ID.

Configure:

```text
ALLOWED_FOUNDRY_CALLER_OBJECT_ID =
<FOUNDRY-PROJECT-IDENTITY-OBJECT-ID>
```

Conceptually:

```text
Foundry Project
       │
       │ Managed Identity
       ▼
Object ID = abc123...
```

AzureExploreAgent then authorizes:

```text
token.oid == abc123...
```

## Agent Identity

When using a specific agent identity, retrieve that identity's Object ID instead.

Configure that Object ID as the permitted caller.

This provides more granular authorization because one published agent can be authorized independently from others.

---

# 19. Step 13 — Explain the inbound token validation

The README should explain exactly what AzureExploreAgent checks.

Conceptually:

```text
aud
=
api://<AZURE-EXPLORE-CLIENT-ID>
```

```text
tid
=
expected tenant
```

```text
oid
=
configured Foundry identity Object ID
```

Illustrative validation:

```csharp
var tokenTenantId =
    user.FindFirstValue("tid");

var tokenObjectId =
    user.FindFirstValue("oid");

var allowed =
    tokenTenantId == expectedTenantId
    &&
    tokenObjectId == allowedFoundryObjectId;
```

The implementation can be more robust, but the README should communicate this security model.

Explain the result:

```text
Token missing
→ 401

Invalid token
→ 401

Wrong audience
→ 401

Correct token but unauthorized Foundry oid
→ 403

Correct token and correct caller
→ A2A execution
```

---

# 20. Step 14 — Configure the Foundry A2A connection

Set the correct Foundry project:

```bash
PROJECT_ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"

azd ai project set "$PROJECT_ENDPOINT"
```

For Project Managed Identity:

```bash
azd ai connection create azure-explore-agent \
  --kind remote-a2a \
  --target "https://<AZURE-EXPLORE-AGENT-HOST>" \
  --auth-type project-managed-identity \
  --audience "api://<AZURE-EXPLORE-CLIENT-ID>"
```

For Agent Identity:

```bash
azd ai connection create azure-explore-agent \
  --kind remote-a2a \
  --target "https://<AZURE-EXPLORE-AGENT-HOST>" \
  --auth-type agentic-identity \
  --audience "api://<AZURE-EXPLORE-CLIENT-ID>"
```

Explain the relationship:

```text
--target
Where AzureExploreAgent is running.

--auth-type
Which Foundry identity requests the token.

--audience
Which protected API the token is intended for.
```

The configured audience must match the AzureExploreAgent Application ID URI.

---

# 21. Step 15 — Agent Card behavior

Explain that A2A uses an Agent Card so Foundry can discover metadata about the remote agent.

The recommended default behavior is:

```text
Agent Card
anonymous/readable

A2A execution endpoint
authenticated
```

Conceptually:

```text
GET agent card
→ 200

POST A2A execution without token
→ 401
```

If the implementation intentionally protects the Agent Card too, Foundry must be configured to send credentials when retrieving it.

For the default AzureExploreAgent setup, prefer keeping discovery public while protecting execution.

---

# 22. Step 16 — Add AzureExploreAgent to the calling Foundry agent

After the connection exists, configure the calling Foundry agent to use the remote A2A connection as a tool.

Explain conceptually:

```text
Calling Foundry Agent
        │
        └── A2A Tool
              │
              └── AzureExploreAgent connection
```

When the model determines AzureExploreAgent is needed:

```text
Foundry Agent
      │
      ▼
obtain Entra token
      │
      ▼
call AzureExploreAgent
      │
      ▼
receive Markdown result
```

The exact portal or SDK configuration may evolve, so the README should reference the current official Foundry A2A documentation.

---

# 23. Step 17 — Validate the deployment

Provide a validation checklist.

First validate AzureExploreAgent itself.

Confirm:

```text
Application starts successfully.

Azure CLI is installed.

Service principal authentication succeeds.

Configured Azure subscription can be selected.

Foundry model can be accessed.

Microsoft Learn MCP can be reached.

Agent Card can be retrieved.

A2A endpoint rejects anonymous execution.
```

Then validate Azure permissions.

Ask AzureExploreAgent something simple such as:

```text
List the Resource Groups you can access.
```

or:

```text
List the resources available in the configured Resource Group.
```

The answer should come from the real Azure environment.

---

# 24. Step 18 — Validate Foundry → AzureExploreAgent

From the calling Foundry agent, use a prompt such as:

```text
Use AzureExploreAgent to list the App Services available
in the Azure environment.
```

Expected flow:

```text
Foundry Agent
      │
      ▼
Project/Agent Identity
      │
      ▼
Entra token
      │
      ▼
AzureExploreAgent
      │
      ▼
Azure CLI
      │
      ▼
Azure
      │
      ▼
Markdown result
      │
      ▼
Foundry Agent
```

---

# 25. Example end-to-end question

Include a more interesting example:

```text
Why might my App Service be unable to communicate with
a private Microsoft Foundry endpoint?
```

Explain that AzureExploreAgent may:

```text
1. Inspect the App Service.

2. Inspect outbound/VNet Integration.

3. Inspect the selected subnet.

4. Inspect private endpoints.

5. Inspect available private DNS configuration.

6. Query Microsoft Learn for documented private networking behavior.

7. Compare the actual environment with the documentation.

8. Return a Markdown analysis.
```

This demonstrates the real value of the repository.

---

# 26. Explain the security model

Include a dedicated security section.

The security model is:

```text
Inbound API security
    Microsoft Entra
    +
    audience validation
    +
    tenant validation
    +
    explicit Foundry oid ACL
```

```text
Foundry model security
    Service Principal
    +
    Foundry RBAC
```

```text
Azure environment security
    Service Principal
    +
    Azure RBAC
```

```text
Azure CLI security
    controlled read-only tool
    +
    no arbitrary shell
```

Emphasize:

> The LLM does not determine which Azure resources it is allowed to access. Azure RBAC does.

And:

> The LLM never receives the AzureExploreAgent Client Secret.

---

# 27. Troubleshooting section

The README should contain at least these cases.

## 401 when Foundry calls AzureExploreAgent

Check:

```text
Application ID URI

Foundry connection audience

Tenant ID

Bearer token configuration
```

Common cause:

```text
Foundry audience != API Application ID URI
```

---

## 403 when Foundry calls AzureExploreAgent

Authentication succeeded but authorization failed.

Check:

```text
oid from the access token

configured allowed Foundry Object ID
```

The Foundry identity calling the application may differ from the identity expected by the application.

---

## Agent Card works but A2A execution fails

This is expected when the Agent Card is public but execution is protected.

Verify the A2A connection authentication configuration.

---

## Foundry model returns authorization errors

Check that the AzureExploreAgent service principal has the required Foundry role at the correct project/resource scope.

---

## Azure CLI returns AuthorizationFailed

The application successfully authenticated, but its service principal does not have Azure RBAC permission for the requested resource.

Verify:

```text
Service Principal Object ID

Reader assignment

RBAC scope
```

Do not solve this by automatically granting Contributor.

---

## Azure CLI executable not found

The hosting environment must contain Azure CLI and expose the `az` executable.

---

## Azure CLI sees the wrong subscription

Verify the configured subscription and the application-owned CLI authentication context.

---

## Microsoft Learn is unavailable

AzureExploreAgent may still answer using the model and Azure CLI, but documentation-dependent answers may be limited.

---

# 28. Customizing the agent

Explain that the repository allows the agent instructions to be customized independently from the integration code.

A fork can therefore change behavior such as:

```text
focus on networking

focus on FinOps

focus on App Service

focus on AKS

focus on security

focus on Foundry architecture
```

while preserving the same:

```text
A2A
Entra authentication
Foundry integration
Learn MCP
Azure CLI
RBAC model
```

---

# 29. Suggested README conclusion

End the README by summarizing AzureExploreAgent as:

```text
AzureExploreAgent is a secure A2A Azure investigation agent.

It reasons with Microsoft Foundry.

It learns from Microsoft Learn.

It observes the real Azure environment through Azure CLI.

It is restricted by Microsoft Entra ID and Azure RBAC.

It can be consumed by other agents through A2A.
```

The goal is not to provide an autonomous Azure administrator.

The goal is to provide other agents with a trusted, read-only capability for understanding an Azure environment.

---

# 30. Official documentation section

The README must finish with an **Official documentation** section.

Include official Microsoft documentation for:

```text
Microsoft Foundry A2A authentication

Connecting Foundry to remote A2A agents

Microsoft Foundry RBAC

Microsoft Agent Framework Foundry provider

Microsoft Agent Framework A2A hosting

Microsoft Learn MCP

Protecting ASP.NET Core APIs with Microsoft Entra ID

Microsoft identity platform Application ID URI

Azure CLI service principal authentication

Azure RBAC role assignments

Azure AI Projects .NET SDK
```

Also include the AzureBrasil.cloud article as the reference implementation for the `aud + tid + oid` authorization pattern used as the basis for AzureExploreAgent's inbound authentication.

The Microsoft documentation must be considered the source of truth if SDK APIs or Foundry configuration behavior changes over time.