# AzureExploreAgent

A read only Azure investigation agent that other agents call through **A2A**. Built with **ASP.NET Core**, **Microsoft Agent Framework**, **Microsoft Foundry**, **Microsoft Entra ID**, **Microsoft Learn MCP**, **Azure CLI** and **Azure RBAC**.

Another agent asks a natural language question ("Which private endpoints exist in this subscription?", "Why can't this App Service reach my private Foundry endpoint?") and gets a Markdown answer that combines:

```text
REASON         Microsoft Foundry model
LEARN          Microsoft Learn MCP     (official documentation)
OBSERVE        Azure CLI               (real configuration, read only)
INTEROPERATE   A2A
```

The agent runs **inside this ASP.NET Core app**. Foundry only supplies the model; you do not need a persistent Foundry Agent Service agent.

## Repository layout

* `src/AzureExploreAgent.Api`: ASP.NET Core host, Entra JWT validation, `FoundryCallerOnly` policy, Agent Card and A2A endpoint. `Instructions/azure-explore-agent.md` holds the editable agent instructions.
* `src/AzureExploreAgent.Application`: agent composition, read only Azure CLI policy, `execute_azure_cli` tool, Learn tool discovery.
* `src/AzureExploreAgent.Infrastructure`: service principal credential, Foundry client, Learn MCP client, Azure CLI runner and login.
* `src/AzureExploreAgent.Tests`: unit tests (no Azure, Foundry, Learn or `az` needed).
* `Dockerfile`: Linux image that includes the Azure CLI.

## How it works

```text
Foundry agent ──token──► AzureExploreAgent (A2A) ──► Foundry model   (reasoning)
                          validates aud, tid, oid  ├─► Learn MCP       (docs)
                                                   └─► az CLI ──► Azure (read only, RBAC-limited)
```

Two independent authentication flows:

| Flow | Who | How |
| --- | --- | --- |
| **Inbound** | Foundry calls AzureExploreAgent | Entra token. The app checks `aud` (this API), `tid` (your tenant) and `oid` (an explicit allow list of Foundry identities). A valid token alone is not enough. |
| **Outbound** | AzureExploreAgent calls Foundry and Azure | A service principal (tenant ID + client ID + client secret) with *Foundry User* on the Foundry project and *Reader* on the Azure scope you choose. |

> The LLM never decides what it can see: **Azure RBAC does**. The LLM never receives the client secret, tokens or environment variables.

## Prerequisites

* An Azure subscription, a Microsoft Entra tenant, and permission to create an App Registration and assign RBAC roles
* A Microsoft Foundry project with a deployed model
* A Foundry agent that will call AzureExploreAgent through A2A
* **.NET SDK** for `net10.0`
* **Azure CLI (`az`) on the host that runs the app. Required.** The app starts `az` as a child process; it is neither bundled nor downloaded. The provided `Dockerfile` installs it. Outside Docker, install it yourself, or point `AzureCli:ExecutablePath` at it.
* **Azure Developer CLI (`azd`)** with the `ai` commands, to create the Foundry A2A connection

The host needs outbound access to Entra ID, Foundry, Azure Resource Manager and `https://learn.microsoft.com/api/mcp`.

## Setup

### 1. App Registration (Entra ID)

Create a single tenant App Registration named `AzureExploreAgent`. Record the **Application (client) ID** and **Directory (tenant) ID**.

* Under **Expose an API**, set the **Application ID URI** to `api://<APPLICATION-CLIENT-ID>`. This is the audience Foundry requests. If it does not match the Foundry connection audience, you get a 401.
* Under **Certificates & secrets**, create a client secret and copy its **value** (shown once). Never commit it.

### 2. Find the service principal Object ID

RBAC is granted to the **Enterprise Application's Object ID**, not the client ID:

```bash
az ad sp show --id "<APPLICATION-CLIENT-ID>" --query id -o tsv
```

### 3. Grant Foundry access

Grant **Foundry User** on the Foundry project (smallest scope that allows inference). Use the role definition ID, since role names have been renamed:

```bash
PROJECT_ID=$(az cognitiveservices account project show \
  --name "<FOUNDRY-ACCOUNT>" --resource-group "<FOUNDRY-RESOURCE-GROUP>" \
  --project-name "<FOUNDRY-PROJECT>" --query id -o tsv)

az role assignment create \
  --assignee-object-id "<SERVICE-PRINCIPAL-OBJECT-ID>" \
  --assignee-principal-type ServicePrincipal \
  --role "53ca6127-db72-4b80-b1b0-d745d6d5456d" \
  --scope "$PROJECT_ID"
```

Do not grant Owner or Contributor.

### 4. Grant Azure read access

Grant **Reader** at the scope the agent may inspect: a resource, a resource group or a subscription. This is the security boundary.

```bash
az role assignment create \
  --assignee-object-id "<SERVICE-PRINCIPAL-OBJECT-ID>" \
  --assignee-principal-type ServicePrincipal \
  --role "Reader" \
  --scope "/subscriptions/<SUBSCRIPTION-ID>/resourceGroups/<RESOURCE-GROUP>"
```

### 5. Find the Foundry caller identity

Add the **Object ID** of the Foundry identity that will call the agent to `FoundryCaller:AllowedObjectIds`. For the project managed identity:

```bash
az cognitiveservices account project show \
  --name "<FOUNDRY-ACCOUNT>" --resource-group "<FOUNDRY-RESOURCE-GROUP>" \
  --project-name "<FOUNDRY-PROJECT>" --query identity.principalId -o tsv
```

After publishing, an agent gets its own identity; use that Object ID to authorize one agent only. Several IDs can be allowed.

### 6. Configure the app

Standard ASP.NET Core configuration: `appsettings.json`, user secrets or environment variables (`__` as separator, e.g. `AzureCli__SubscriptionId`). `appsettings.json` ships with placeholders (`<TENANT-ID>`, `YOUR-ACCOUNT`, ...) that you must override. Keep the secret in user secrets, environment variables or Key Vault.

#### Settings reference

**Inbound** (who may call the agent)

| Setting | What it is | Where to get it |
| --- | --- | --- |
| `AzureAd:TenantId` | Tenant expected in incoming tokens (`tid`) | App Registration overview → Directory (tenant) ID |
| `AzureAd:ClientId` | This API; accepted audience is `api://<ClientId>` | App Registration overview → Application (client) ID |
| `AzureAd:AllowWebApiToBeAuthorizedByACL` | Lets the `oid` allow list authorize callers. Keep `true`: Foundry's app only token has no `scp`/`roles` claim | Already set in `appsettings.json` |
| `FoundryCaller:AllowedObjectIds` | Object IDs (`oid`) allowed to run the agent. At least one | Step 5 |

**Outbound** (who the agent is when it calls Foundry and Azure)

| Setting | What it is | Where to get it |
| --- | --- | --- |
| `ServicePrincipal:TenantId` | Tenant of the service principal | Same as `AzureAd:TenantId` |
| `ServicePrincipal:ClientId` | Identity the agent signs in as | Same as `AzureAd:ClientId` when using one App Registration |
| `ServicePrincipal:ClientSecret` | Secret for that identity | Step 1 (the secret **value**, not the ID). Secret |
| `Foundry:ProjectEndpoint` | Foundry project endpoint, `https://<account>.services.ai.azure.com/api/projects/<project>` | Foundry portal → project → Overview |
| `Foundry:ModelDeploymentName` | Model deployment the agent uses | Foundry portal → Models + endpoints (deployment **name**) |

**Azure CLI** (how the agent inspects Azure)

| Setting | What it is | Where to get it |
| --- | --- | --- |
| `AzureCli:SubscriptionId` | Subscription `az` works against (runs `az account set`). Should contain your Reader scope | `az account list --query "[].{name:name, id:id}" -o table` |
| `AzureCli:ExecutablePath` | The `az` executable. Default `az` (must be on `PATH`) | Full path from `which az` / `where az` if not on `PATH` |
| `AzureCli:ConfigDirectory` | Isolated `AZURE_CONFIG_DIR`, so the agent never uses your personal `az login`. Default: a folder in the system temp dir | Optional. Set it to a writable path (e.g. a mounted volume) in locked-down containers |
| `AzureCli:TimeoutSeconds` | Max time for each `az` query the agent runs. Default 30 | Raise it for slow queries |
| `AzureCli:LoginTimeoutSeconds` | Max time for each startup step (`login`, `account set`, `account show`). Default 90 | Raise it on slow networks |
| `AzureCli:MaxOutputCharacters` | Output above this size is truncated before reaching the model. Default 60000 | Lower it to save model tokens |

**Other**

| Setting | What it is | Where to get it |
| --- | --- | --- |
| `A2A:PublicBaseUrl` | HTTPS base URL advertised in the Agent Card | Where you host the app (locally `http://localhost:5080` or a tunnel URL) |
| `MicrosoftLearn:Endpoint` | Learn MCP server. Default `https://learn.microsoft.com/api/mcp` | Keep the default |
| `MicrosoftLearn:ConnectionTimeoutSeconds` | Startup connection timeout (1 to 300) | Keep the default |
| `AgentInstructions:FilePath` | Agent instructions file | Keep the default unless you fork the instructions |
| `AzureAd:Instance` | Entra authority. Default is Azure public cloud | Change only for sovereign clouds |

Local setup with user secrets:

```bash
cd src/AzureExploreAgent.Api
dotnet user-secrets set "AzureAd:TenantId" "<TENANT-ID>"
dotnet user-secrets set "AzureAd:ClientId" "<APPLICATION-CLIENT-ID>"
dotnet user-secrets set "FoundryCaller:AllowedObjectIds:0" "<FOUNDRY-CALLER-OBJECT-ID>"
dotnet user-secrets set "ServicePrincipal:TenantId" "<TENANT-ID>"
dotnet user-secrets set "ServicePrincipal:ClientId" "<APPLICATION-CLIENT-ID>"
dotnet user-secrets set "ServicePrincipal:ClientSecret" "<CLIENT-SECRET>"
dotnet user-secrets set "AzureCli:SubscriptionId" "<SUBSCRIPTION-ID>"
dotnet user-secrets set "Foundry:ProjectEndpoint" "https://<account>.services.ai.azure.com/api/projects/<project>"
dotnet user-secrets set "Foundry:ModelDeploymentName" "<deployment-name>"
```

## Run

```bash
dotnet test AzureExploreAgent.sln
dotnet run --project src/AzureExploreAgent.Api      # http://localhost:5080
```

```text
GET  /health                                public
GET  /.well-known/agent-card.json           public
POST /a2a/azure-explore-agent[/message:send|/message:stream]    FoundryCallerOnly
```

At startup the app signs in to `az` as the service principal (it fails fast if login, subscription or configuration is wrong), discovers the Learn tools and builds the agent. Foundry cannot reach `localhost`: for an end to end test, deploy or use an HTTPS tunnel and set `A2A:PublicBaseUrl`.

### Run with Docker

The `Dockerfile` builds a Linux image containing the app **and the Azure CLI** (required, see Prerequisites).

```bash
docker build -t azure-explore-agent .

docker run --rm -p 8080:8080 \
  -e AzureAd__TenantId="<TENANT-ID>" \
  -e AzureAd__ClientId="<APPLICATION-CLIENT-ID>" \
  -e AzureCli__SubscriptionId="<SUBSCRIPTION-ID>" \
  azure-explore-agent        # plus every other setting above, e.g. ServicePrincipal__ClientSecret
```

* Listens on `8080`; set `A2A__PublicBaseUrl` to the public HTTPS URL in front of it.
* Never bake the client secret into the image; inject it at runtime.
* Runs as non-root. With a read only root filesystem, mount a writable volume at `AzureCli__ConfigDirectory` (default `/home/app/.azure-explore-agent`).
* Check the CLI: `docker run --rm --entrypoint az azure-explore-agent version`.

## Deploy and connect to Foundry

Any host with the `net10.0` runtime and `az` works (App Service, Container Apps, AKS, VMs). For containers use the `Dockerfile`. The app needs an HTTPS endpoint reachable from Foundry and honors `X-Forwarded-Proto` behind a TLS terminating ingress. A2A sessions are in memory: lost on restart, not shared across instances.

Create the Foundry A2A connection:

```bash
azd ai project set "https://<account>.services.ai.azure.com/api/projects/<project>"

azd ai connection create azure-explore-agent \
  --kind remote-a2a \
  --target "https://<AZURE-EXPLORE-AGENT-HOST>" \
  --auth-type project-managed-identity \
  --audience "api://<APPLICATION-CLIENT-ID>"
```

* `--target`: where the app runs.
* `--auth-type`: which Foundry identity requests the token (`project-managed-identity`, or `agentic-identity` for a published agent's own identity).
* `--audience`: must equal the Application ID URI.

Then attach the connection to the calling agent as an A2A tool. AzureExploreAgent advertises A2A `1.0` (JSON-RPC first, then HTTP+JSON), so use the generally available `a2a` tool, not the `a2a_preview` tool that `azd ai toolbox` creates. See "Connect to an A2A agent endpoint from Foundry Agent Service" in the documentation below.

## Validate

```bash
HOST="https://<AZURE-EXPLORE-AGENT-HOST>"
curl -i "$HOST/health"                                  # 200
curl -s "$HOST/.well-known/agent-card.json"             # Agent Card
curl -i -X POST "$HOST/a2a/azure-explore-agent/message:send" \
  -H "Content-Type: application/json" -d '{}'           # 401
```

Logs should show `Azure CLI authenticated as service principal ...`, `Discovered N Microsoft Learn MCP tools` and `Creating AzureExploreAgent on model ...`. Then ask from the calling agent: *"Use AzureExploreAgent to list the resource groups you can access."* The answer must stay within the Reader scope.

Token validation results: missing, invalid or wrong audience token → **401**; valid token with an `oid` not in the allow list → **403**; allowed `oid` → execution. The Agent Card stays public so Foundry can discover the agent; execution is protected.

## Azure CLI tool safety

The model never gets a shell. The `execute_azure_cli` tool:

* accepts a command group, an operation and named arguments, never a raw string
* runs `az` directly (no shell), so `;`, `&&`, `|`, `>` and `$()` are never interpreted
* allows only `list`, `show`, `get`, `check` and `query`
* blocks commands that read secrets (keys, tokens, connection strings, app settings), `az login`, `az rest`, `az config`, `az ad` and `@file` values
* forces `--output json`, applies a timeout and truncates large output
* redacts the client secret, bearer tokens and JWTs from anything returned to the model

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| 401 from Foundry | Connection `--audience` ≠ Application ID URI, or wrong `AzureAd:TenantId` / `AzureAd:ClientId` |
| 403 from Foundry | Token `oid` not in `FoundryCaller:AllowedObjectIds` (project vs published agent identity). Entra sign in logs show the caller's Object ID |
| Model authorization errors | Missing *Foundry User* for the service principal at the right scope |
| `az` returns AuthorizationFailed | Service principal lacks Reader there. Do not fix with Contributor |
| `az` executable not found | Install it, set `AzureCli:ExecutablePath`, or build from the `Dockerfile` |
| `az` sees the wrong subscription | Check `AzureCli:SubscriptionId`. Your personal `az` session is not used |
| Learn unavailable | The agent still starts with only the CLI tool; restart once Learn is reachable |

## Customizing

Edit `src/AzureExploreAgent.Api/Instructions/azure-explore-agent.md` (path via `AgentInstructions:FilePath`) to focus a fork on networking, FinOps, App Service, AKS, security or Foundry architecture. The A2A hosting, Entra authentication, CLI policy and RBAC model stay the same.

AzureExploreAgent is not an autonomous Azure administrator: it gives other agents a trusted, read only way to understand an Azure environment.

## Official documentation

The Microsoft documentation is the source of truth if SDK APIs or Foundry behavior change.

* Foundry A2A authentication: https://learn.microsoft.com/azure/foundry/agents/concepts/agent-to-agent-authentication
* Connect Foundry to remote A2A agents: https://learn.microsoft.com/azure/foundry/agents/how-to/tools/agent-to-agent
* Foundry agent identity: https://learn.microsoft.com/azure/foundry/agents/concepts/agent-identity
* Foundry RBAC: https://learn.microsoft.com/azure/foundry/concepts/rbac-foundry
* Agent Framework Foundry provider: https://learn.microsoft.com/agent-framework/integrations/by-component/model-providers/microsoft-foundry
* Agent Framework A2A hosting: https://learn.microsoft.com/agent-framework/hosting/self-hosting/a2a/server
* Microsoft Learn MCP Server: https://learn.microsoft.com/training/support/mcp
* Protected web API configuration: https://learn.microsoft.com/entra/identity-platform/scenario-protected-web-api-app-configuration
* Expose a web API (Application ID URI): https://learn.microsoft.com/entra/identity-platform/quickstart-configure-app-expose-web-apis
* Access token claims reference: https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference
* Azure CLI service principal sign in: https://learn.microsoft.com/cli/azure/authenticate-azure-cli-service-principal
* Assign Azure roles using Azure CLI: https://learn.microsoft.com/azure/role-based-access-control/role-assignments-cli
* Azure AI Projects client library for .NET: https://learn.microsoft.com/dotnet/api/overview/azure/ai.projects-readme
* Reference implementation of the aud + tid + oid pattern (AzureBrasil.cloud), "Protegendo um MCP Server com Microsoft Entra ID e consumindo ele a partir do Microsoft Foundry": <ARTICLE-URL>
