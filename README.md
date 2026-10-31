# MCP Module

## Overview

The MCP module connects AI agents such as Claude, ChatGPT or your own agent to the Virto Commerce platform through the [Model Context Protocol](https://modelcontextprotocol.io). It is the platform-level MCP infrastructure: it hosts the MCP endpoints, signs users in with the platform's own OAuth, checks permissions, runs every tool call through a common pipeline, holds risky changes for a second person's approval, and audits every call. Feature modules add tools, prompts and resources declaratively and keep to their business logic.

An agent works as the signed-in user: it sees only the tools that user may use, limited to the user's stores, and a change it asks for can wait in the back office until an approver applies it. Built on the official C# SDK, [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore).

## Key Features

* **Scopes**: one MCP endpoint per kind of agent (`admin`, `vendor`, a buyer scope, your own), at `/{scope}/mcp` or at `/mcp` of a host of its own
* **Platform sign-in**: OAuth 2.1 with PKCE and a token bound to one endpoint (RFC 8707); anonymous scopes with sign-in on demand per tool; revoked sessions stop working on the next request
* **Permissions**: `[Authorize]` for platform permissions, `[McpPermission]` for permissions limited to stores or sellers; tools the user may not use are not listed
* **Approval with a diff**: `[McpRequiresApproval]` holds a call as a pending change with a dry-run preview until a second person approves it, always or only when a condition on the arguments says so
* **Audit**: every call with user, client, arguments, outcome, duration and trace id; `[McpSensitive]` masks personal data
* **Bulk jobs**: a bulk tool hands its work to a platform background job; the agent follows and cancels it
* **Kill switch**: switch a tool, or a whole scope, off without a release
* **Back office**: Admin Shell screens for the approval queue, the audit log and the tool switches, for desktop and phone
* **Observability**: OpenTelemetry traces, metrics and logs of tool calls, reviews and jobs
* **Guard rails for agents**: a row limit for list tools, errors the model can correct, a common result shape for previews
* **Multi-database support**: SQL Server, MySQL and PostgreSQL

Not in the module, by design: rate limits and CORS, which belong to the infrastructure in front of the platform (see [Rate limiting and CORS](#rate-limiting-and-cors)).

## Connect an AI agent

An agent connects to an MCP endpoint of the platform and signs in as a platform user. The steps below connect an agent to the back-office endpoint, `/admin/mcp`, of a platform at `https://virtostart-demo-admin.govirto.com`; replace it with the public address of your platform. The endpoint is always the platform's public HTTPS address followed by `/admin/mcp`.

### 1. Prepare the platform (administrator, once)

1. Install `VirtoCommerce.MCP` (and `VirtoCommerce.BackgroundJobs`, for bulk tools), plus the modules that bring tools.
1. Allow the platform to issue tokens for the endpoint, in `appsettings.json`:

    ```json
    "Authorization": {
      "Resources": [ "https://virtostart-demo-admin.govirto.com/admin/mcp" ]
    }
    ```

1. Register the agent as an OAuth client with `POST https://virtostart-demo-admin.govirto.com/api/platform/oauthapps` (Swagger at `/docs`). It is a public client: the agent holds no secret, only the user's token. The redirect URI is where the agent receives the sign-in, and explicit consent shows the user which agent they let act as them, once per user and agent:

    ```json
    {
      "clientId": "claude-code",
      "displayName": "Claude Code",
      "clientType": "public",
      "consentType": "explicit",
      "redirectUris": [ "http://localhost:65111/callback" ],
      "requirements": [ "ft:pkce" ],
      "permissions": [ "rsrc:https://virtostart-demo-admin.govirto.com/admin/mcp" ]
    }
    ```

    `http://localhost:65111/callback` is not the platform: it is the address where Claude Code, on the user's own computer, receives the result of the sign-in. Keep it as it is for Claude Code; other agents have their own callback address, for example `https://claude.ai/api/mcp/auth_callback` for claude.ai.

1. Grant permissions with roles: `mcp:access` to everyone who connects an agent, `mcp:approve` to approvers, `mcp:audit:read`, `mcp:tools:manage` and `mcp:agents:manage` to the people who run the agents, plus the permissions of the tools each person uses (for example `marketing:read`, limited to their stores).

> [!NOTE]
> Registering each kind of agent by hand is a stopgap. The MCP specification prefers clients that identify themselves with a client ID metadata document, so that a user connects with the endpoint URL alone, as with Shopify, BigCommerce or commercetools; the platform does not support it yet ([VCST-6639](https://virtocommerce.atlassian.net/browse/VCST-6639)). Until then, also note that `POST /api/platform/oauthapps` adds default permissions to every client (`client_credentials`, `email`, `profile`, end session). An agent needs only the authorization code and refresh token grants and the `rsrc:` resource; the extra grants give a public client no access, since it holds no secret.

### 2. Connect the agent (each user)

**Claude Code**

```bash
claude mcp add --transport http --client-id claude-code --callback-port 65111 virto https://virtostart-demo-admin.govirto.com/admin/mcp
```

Then in Claude Code run `/mcp`, select `virto` and choose **Authenticate**. The platform sign-in page opens in the browser; after the sign-in the agent lists the tools the user may use. Add `--scope project` to keep the server in the folder's `.mcp.json`, to share it with a team; the token stays in the user's profile. The callback port must match the redirect URI of the client.

**Claude Desktop and claude.ai**

Add a custom connector (**Settings → Connectors → Add custom connector**) with the URL `https://virtostart-demo-admin.govirto.com/admin/mcp` and, under the advanced settings, the OAuth client id. Claude connects from Anthropic's cloud, so the endpoint must be reachable from the internet over HTTPS, and the client must be registered with Claude's callback URL (`https://claude.ai/api/mcp/auth_callback` at the time of writing; check the current Claude documentation).

**Other MCP clients**

Any client that supports the streamable HTTP transport and OAuth with PKCE and a pre-registered client id works the same way: ChatGPT connectors, VS Code, Cursor, the MCP Inspector or your own agent built with an MCP SDK. Register one OAuth client for each, with that client's redirect URI.

### Package a connector for a solution (system integrators)

A system integrator ships "Virto Commerce for Contoso" to a client's Claude organization as a connector, so the client's people click **Connect** and sign in, with no setup of their own:

1. **Toolset.** Put the solution's tools, prompts and resources in a `VirtoCommerce.<Solution>.MCP` project (see [Add tools to a scope](#add-tools-to-a-scope)), protected by the client's roles; extend the standard toolsets as described in [How to extend](#how-to-extend).
1. **Name and instructions.** Register the `admin` scope again with the solution's title and instructions; Claude shows them as the server's name and reads the instructions before using the tools:

    ```csharp
    serviceCollection.AddMcpScope(new McpScope
    {
        Name = McpScopes.Admin,
        Title = "Contoso Commerce",
        Instructions = "Contoso sells office supplies in three stores. Prices are in USD. Always run a dry run first...",
        RequireAuthenticatedUser = true,
        Permissions = [ModuleConstants.Security.Permissions.Access],
        ApprovalPermission = ModuleConstants.Security.Permissions.Approve,
    });
    ```

1. **Address.** Serve the endpoint on a public HTTPS host, for example `https://virtostart-demo-admin.govirto.com/admin/mcp`, or give it a host of its own with `MCP:Scopes:admin:Hosts` (`https://mcp.contoso.com/mcp`), and add it to `Authorization:Resources`.
1. **OAuth client for Claude.** Register one client with Claude's callback URL (`https://claude.ai/api/mcp/auth_callback`) and explicit consent, as in [step 1](#1-prepare-the-platform-administrator-once).
1. **Roles.** Grant the client's people `mcp:access` and the permissions of their tools, limited to their stores; approvers get `mcp:approve`.
1. **Claude organization.** An owner of the client's Claude Team or Enterprise organization adds a custom connector for everyone, with the URL and the OAuth client id (organization settings, connectors; the exact menu names change, check the current Claude documentation). Each person then connects once and signs in with their Virto account.
1. **Routines.** The scope's MCP prompts appear in Claude as ready-made routines (for example a weekly review); longer playbooks can also be shared with the organization as Claude skills.
1. **Operations.** The client's administrators use the back office: approvals, the audit log, tool switches and connected agents.

A listing in Claude's public connector directory is for a product every Virto Commerce customer could use, and goes through Anthropic's review; a client's own solution stays a custom connector of that client's organization. Once the platform supports client ID metadata documents ([VCST-6639](https://virtocommerce.atlassian.net/browse/VCST-6639)), the OAuth client step goes away: the URL alone is enough.

### 3. Check and manage the connection

Ask the agent "What can you do in Virto?" or call a read-only tool. In the back office, **Configuration → AI agents (MCP) → Agent audit log** shows the call with the user and the client id, and **Connected agents** lists the agents each user signed in to, with when they were last used. **Disconnect** revokes the agent's sign-in: its next request is refused, and the user signs in again to reconnect. Users see and disconnect their own agents; `mcp:agents:manage` shows everyone's.

| Symptom | Cause |
| --- | --- |
| The sign-in page does not open, or the sign-in ends with an error page | The redirect URI or the callback port of the client does not match its registration |
| `invalid_token` after the sign-in | The token was issued for another address: the URL the agent uses must be listed in `Authorization:Resources` and in the client's `rsrc:` permission exactly, scheme and port included |
| HTTP 403 | The user lacks the scope's permission, `mcp:access` for `/admin/mcp` |
| The agent sees fewer tools than expected | Tools the user has no permission for are not listed; a tool switched off in **Agent tools** is not listed either |

[Demo: an AI agent in the back office](docs/demo.md) walks through all of this on a local platform with Claude Code.

## Configuration

### appsettings.json

```json
"MCP": {
  "PublicOrigin": "https://admin.example.com",
  "Scopes": {
    "vendor": { "Enabled": false },
    "ucp": { "PublicOrigin": "https://store.example.com", "Hosts": [ "store.example.com" ] }
  }
}
```

| Key | Description |
| --- | --- |
| `MCP:PublicOrigin` | Origin advertised in the OAuth metadata and expected in token audiences. Defaults to the origin of the request. |
| `MCP:Scopes:{scope}:Enabled` | Switches a scope off. Defaults to `true`. |
| `MCP:Scopes:{scope}:PublicOrigin` | Origin of one scope, for example the storefront host for a buyer scope. |
| `MCP:Scopes:{scope}:Hosts` | Hosts that serve the scope at `/mcp`, for example `store.example.com`. An entry with a port matches that port only. A host serves one scope; a host listed for two scopes stops the platform at startup. Add `https://{host}/mcp` to `Authorization:Resources`. |

Invalid origins stop the platform at startup: an origin is an absolute `http` or `https` address without a path, query or fragment.

> [!NOTE]
> On a deployment where `Authorization:OAuthLoginPath` points to the storefront login page, the platform host needs the per-host login path from [VirtoCommerce/vc-platform#3121](https://github.com/VirtoCommerce/vc-platform/pull/3121) for the `admin` scope sign-in to work.

### Application Settings

| Setting | Type | Default | Description |
| --- | --- | --- | --- |
| `MCP.Approval.AllowSelfApproval` | Boolean | `false` | Allow a user to approve their own change. |
| `MCP.Approval.PendingChangeLifetimeHours` | Integer | `72` | Lifetime of a pending change. |
| `MCP.Audit.Enabled` | Boolean | `true` | Audit tool calls made through MCP scopes. |
| `MCP.Audit.LogArguments` | Boolean | `true` | Store tool call arguments in the audit log. |
| `MCP.Cleanup.Enabled` | Boolean | `true` | Run the recurring cleanup job (`MCP.Cleanup`). |
| `MCP.Cleanup.Cron` | Cron | `0 3 * * *` | Cleanup schedule, in UTC. |
| `MCP.Cleanup.AuditRetentionDays` | Integer | `90` | Audit entries and finished jobs older than this are deleted. |
| `MCP.Cleanup.PendingChangeRetentionDays` | Integer | `90` | Applied, failed, rejected and expired changes older than this are deleted. An approved change that never completed is kept for investigation. |
| `MCP.Tools.Disabled` | Long text | empty | Tools switched off, one per line: `tool`, `scope/tool` or `scope/*`. The **Agent tools** screen writes it. |
| `MCP.Tools.MaxTake` | Integer | `50` | Largest number of rows a list tool returns in one call. A tool that is not given a number returns 20 rows, or fewer if this limit is lower. |

### Permissions

| Permission | Description |
| --- | --- |
| `mcp:access` | Connect AI agents to the `admin` scope |
| `mcp:approve` | Review, approve and reject changes submitted in the `admin` scope. Other scopes name their own approver permission. |
| `mcp:audit:read` | Read the tool call audit log |
| `mcp:tools:manage` | See the tools of every scope and switch them on and off |
| `mcp:agents:manage` | See and disconnect the AI agents of every user; without it, users see and disconnect their own |

### Observability

The audit log is the business record of who did what, kept in the database for the back office and for compliance. For operations, the module emits OpenTelemetry signals with the .NET APIs (`ActivitySource`, `Meter`, `ILogger`), so it needs no OpenTelemetry package; the [OpenTelemetry module](https://github.com/VirtoCommerce/vc-module-opentelemetry) exports them when the deployment opts in:

```json
"OpenTelemetry": {
  "Enabled": true,
  "Endpoint": "http://collector:4317",
  "Sources": [ "VirtoCommerce.MCP", "Experimental.ModelContextProtocol" ],
  "Meters": [ "VirtoCommerce.MCP", "Experimental.ModelContextProtocol" ]
}
```

| Signal | Name | Tags |
| --- | --- | --- |
| Span per tool call | `MCP tool {tool}` | `vc.mcp.scope`, `gen_ai.tool.name`, `mcp.method.name`, `vc.mcp.client_id`, `vc.mcp.outcome`, `vc.mcp.pending_change_id`; status `Error` for `Failed` and `Error` |
| Span per back-office review | `MCP review {decision} {tool}` | `vc.mcp.approval.decision`, `vc.mcp.review.channel`, plus the tool's span inside it |
| Counter | `vc.mcp.tool.calls` | scope, tool, outcome |
| Histogram, seconds | `vc.mcp.tool.duration` | scope, tool, outcome |
| Counter | `vc.mcp.approval.decisions` | scope, tool, decision (`submitted`, `approved`, `applied`, `failed`, `rejected`, `expired`) |
| Counter | `vc.mcp.jobs` | final status |
| Log | `MCP tool {ToolName} called in scope {ScopeName} by {UserName}: {Outcome} in {DurationMs} ms` | with the trace and span ids |

`Experimental.ModelContextProtocol` adds the SDK's own spans and metrics for the JSON-RPC requests (`tools/call {tool}`, `mcp.server.operation.duration`). With the platform's ASP.NET Core and EF Core instrumentation, one tool call is one trace: the HTTP request, the SDK span, the tool span and the database queries inside it. The trace id is returned to the agent in `_meta.trace_id` and stored on the audit entry, so a user's report, the audit log and the traces lead to each other.

### Rate limiting and CORS

The module does not limit the request rate and does not add CORS headers: both belong to the infrastructure in front of the platform. Limit the rate of requests to `/*/mcp` in the API gateway or the load balancer, because an agent that goes into a loop can call a tool hundreds of times a minute. CORS is needed only for MCP clients that run in a browser; Claude, ChatGPT and other hosted clients call the server from their own back end.

## Architecture

```
┌────────────────────────────────────────────────────────────────────────────┐
│  AI agents: Claude, ChatGPT, IDEs, your own (OAuth 2.1 + PKCE)             │
├────────────────────────────────────────────────────────────────────────────┤
│  MCP endpoints  /admin/mcp · /vendor/mcp · /{scope}/mcp · /mcp of a host   │
├────────────────────────────────────────────────────────────────────────────┤
│  MCP module: scope, token, permissions, tool switches, approval, audit,    │
│  jobs, telemetry · Admin Shell plugin · REST API                           │
├────────────────────────────────────────────────────────────────────────────┤
│  Tools, prompts and resources of feature modules (Marketing, Catalog, ...) │
├────────────────────────────────────────────────────────────────────────────┤
│  Module services · background jobs · SQL Server / MySQL / PostgreSQL       │
└────────────────────────────────────────────────────────────────────────────┘
```

### Tool call flow

1. The agent sends `tools/call` to a scope's endpoint with the user's bearer token.
1. The module resolves the scope from the path or the host, and checks that the token was issued for this endpoint and is still valid in the platform token store.
1. The scope's permission and the tool's permissions (`[Authorize]`, `[McpPermission]`) are checked. A tool switched off with `MCP.Tools.Disabled` is refused.
1. Required arguments are validated. A missing argument is returned as the tool's error, not a protocol error, so the model can correct the call.
1. A tool marked `[McpRequiresApproval]` does not run: if it has a `dryRun` (or `dry_run`) parameter it runs once as a dry run, and the call is stored as a pending change with that preview. With a `Condition`, only the calls the condition selects are held.
1. Otherwise the tool runs as the user and calls the module's services.
1. The call is audited (user, OAuth client, tool, arguments, outcome, duration, trace id), logged and traced.
1. An approver reviews the pending change in the back office or with the review tools; approving runs the tool with the stored arguments, as the approver.

A tool refuses a request with `McpException` (audited as `Failed`) or `McpForbiddenException` (audited as `Forbidden`): the agent gets the message as it is, as the tool's error. Every failed call returns the trace id in `_meta.trace_id`.

### Scopes

A scope is an MCP surface for one kind of agent. Only the tools, prompts and resources marked for a scope are visible and callable on it.

| Scope | Endpoint | Registered by | Who connects |
| --- | --- | --- | --- |
| `admin` | `/admin/mcp` | this module | back-office users with the `mcp:access` permission |
| `vendor` | `/vendor/mcp` | this module | vendor portal users with the `vendor_portal:access` permission (defined by the marketplace vendor module; without it the scope refuses everyone) |
| `ucp` | `/ucp/mcp` | the UCP module, from its VCST-6155 version ([Moving UCP to the MCP module](docs/ucp-migration.md)); earlier UCP versions map the endpoint themselves (see [Coexistence](#coexistence-with-modules-that-host-their-own-mcp-endpoint)) | buyers and shopping agents |

Every request is stateless: the scope and the user are resolved per HTTP request, so replicas need no session affinity and one deployment serves all scopes side by side.

A scope can also be served at `/mcp` on hosts of its own, for example the storefront host for a buyer scope, set with `MCP:Scopes:{scope}:Hosts`; the scope stays available at `/{scope}/mcp` as well. At `/mcp` of a host, the OAuth resource is that host's address, so a token for `https://store.example.com/mcp` is not accepted at `https://store.example.com/ucp/mcp`, and the other way round.

### Authentication

MCP endpoints accept only bearer tokens issued by the platform for the exact MCP resource (RFC 8707 audience). Cookies, API keys and basic authentication are ignored on them. An anonymous request to a scope that requires a user gets an HTTP 401 challenge pointing to the scope's protected resource metadata (RFC 9728), which starts the platform OAuth flow in the MCP client:

```
GET /.well-known/oauth-protected-resource/{scope}/mcp
```

On every request the token is also looked up in the platform token store, so a revoked token or sign-in session stops working on all replicas right away. A tool that signs the user out, such as a buyer's `logout`, calls `IMcpSessionService.SignOutAsync(user)`.

> [!NOTE]
> A token without an id, issued while the platform token storage is disabled, cannot be looked up, so its revocation is not checked: it works until it expires. Keep the token storage enabled on deployments with MCP scopes.

### Approval

- By default a change cannot be approved by the user who requested it (`MCP.Approval.AllowSelfApproval`).
- The approver must also be allowed to run the tool, and a tool switched off cannot be approved.
- A change expires after `MCP.Approval.PendingChangeLifetimeHours` and can no longer be approved.
- A change is executed at most once, even if two approvers, or two replicas, approve it at the same time.
- A call with `dryRun: true` changes nothing, so it runs right away instead of being submitted.
- Changes are approved in scopes with approval only (`McpScope.ApprovalPermission`; `admin` by default). In any other scope, a call to a tool marked `[McpRequiresApproval]` is refused, and the tool is reported as an error in the log at startup. So is such a tool without `[McpScope]`, because on endpoints without a scope its calls are not held for approval.
- An approved change runs to the end and its result is recorded, even if the approver's client disconnects.

Changes are reviewed in the back office or with these tools, present in every scope with approval. An approver of a scope works with every change of that scope; any other user sees and withdraws only the changes they requested.

| Tool | Purpose | Who |
| --- | --- | --- |
| `list_pending_changes` | Compact list of submitted changes, by status | approvers: all changes; others: their own |
| `get_pending_change` | Arguments, dry-run preview, reviewer and result of one change | approvers: any change; others: their own |
| `approve_pending_change` | Approves the change and runs the tool with the stored arguments, as the approver | approvers only |
| `reject_pending_change` | Rejects the change; the requester uses it to withdraw their own change | approvers: any change; others: their own |
| `get_job_status` | Status and progress of a background job started by a bulk tool; also in the `vendor` scope | the user who started it, the requester of the change that started it, approvers |
| `cancel_job` | Cancels a queued or running background job; also in the `vendor` scope | as `get_job_status` |

### Audit and cleanup

Every call made through a registered scope is audited with one of the outcomes `Succeeded`, `PendingApproval`, `Forbidden`, `Disabled` (the tool is switched off), `Failed` (the tool returned an error or refused the request with an `McpException`) or `Error` (unknown tool, invalid arguments, unexpected error). Reviews made in the back office are audited with the client id `back-office`. A tool that acts on a pending change links its audit entry to it with `requestContext.SetAuditPendingChangeId(changeId)`.

The recurring job `MCP.Cleanup` deletes audit entries, finished jobs, and applied, failed, rejected and expired changes older than the retention settings. An approved change that never completed is kept: it needs a person to look at it. The job also marks overdue pending changes as expired, which removes their sensitive arguments.

### Switching tools off

An administrator switches tools off without a release on the **Agent tools** screen, or with the `MCP.Tools.Disabled` setting, one entry per line:

| Entry | Switches off |
| --- | --- |
| `activate_promotion` | the tool in every scope |
| `admin/activate_promotion` | the tool in the `admin` scope only |
| `vendor/*` | every tool of the `vendor` scope |

A tool that is switched off disappears from `tools/list`, a call to it is refused with a message the agent can pass on to the user and audited as `Disabled`, and its pending changes cannot be approved until it is switched on again. The change applies to the next request, with no restart.

### Back office

The module ships an [Admin Shell](https://github.com/VirtoCommerce/vc-module-admin-shell) plugin: four screens under **Configuration → AI agents (MCP)**, built with VC-Shell and laid out for desktop and phone.

| Screen | Who | What it does |
| --- | --- | --- |
| Agent approvals | approvers of a scope (`mcp:approve`, or the scope's `ApprovalPermission`) | The queue of changes agents asked to make, with a menu counter. A change opens with **what will change**: the object, a *now → after* diff and the warnings of its dry run, then the request and the arguments. **Approve** runs the tool right away as the reviewer, with the same rules as `approve_pending_change`; **Reject** records the reason. Once applied, the screen shows the tool's result as a diff. |
| Agent audit log | `mcp:audit:read` | Every tool call with the user, the client, the scope, the duration and the outcome, filtered by outcome and scope. The search box takes a tool name, or the trace id an agent reported. A call links to the change it submitted or executed. |
| Agent tools | `mcp:tools:manage` | Every tool of every scope with its kind (read, write, needs approval) and a switch; one switch per scope turns a whole scope off. |
| Connected agents | `mcp:access` for one's own agents, `mcp:agents:manage` for everyone's | The agents users signed in to: the OAuth client, the user, the MCP endpoint, when it was connected and last used, and its active sign-ins. **Disconnect** revokes the sign-in at once. |

The plugin is in `src/VirtoCommerce.MCP.Web/vue` (Vue 3, `@vc-shell/framework`, Yarn 4). `dotnet publish`, and so `vc-build Compress`, builds it into the package as `plugins/admin-shell`; `-p:SkipAdminShellPlugin=true` leaves it out. To work on it against a running platform:

```bash
cd src/VirtoCommerce.MCP.Web/vue
yarn install
yarn build    # then copy ../plugins/admin-shell to modules/VirtoCommerce.MCP/plugins/admin-shell; no restart needed
```

### Coexistence with modules that host their own MCP endpoint

A module that maps its own MCP endpoint, as earlier UCP versions do at `/ucp/mcp`, shares the same MCP server. On such an endpoint, primitives marked with `[McpScope]` are removed, so scoped tools never appear where scopes are not enforced. [Moving UCP to the MCP module](docs/ucp-migration.md) describes what moves and what stays in UCP. Both modules must use the same version of the MCP SDK: the platform loads one copy of an assembly, the newest, for all modules.

## Developer guide

### Add tools to a scope

Tools are ordinary SDK tool types. Mark the class or the method with `[McpScope]`, protect it with a platform permission, and register the type from the module's `Initialize`:

```csharp
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[Authorize(MarketingConstants.Security.Permissions.Read)]
public class PromotionTools
{
    [McpServerTool(Name = "search_promotions", ReadOnly = true)]
    [Description("Search promotions. Returns compact rows.")]
    public Task<object> SearchPromotions(IMcpPageSizeResolver pageSizeResolver, string query, int? take = null) { ... }

    [McpServerTool(Name = "activate_promotion", Destructive = true)]
    [Description("Activate a promotion.")]
    [Authorize(MarketingConstants.Security.Permissions.Update)]
    [McpRequiresApproval]
    public Task<object> ActivatePromotion(string promotionId, bool dryRun = false) { ... }
}

// Module.Initialize
serviceCollection.AddMcpTools<PromotionTools>();
```

- `AddMcpPrompts<T>()` and `AddMcpResources<T>()` register prompts and resources the same way. A static class is registered with `AddMcpTools([typeof(MyTools)])`; every class of an assembly with `AddMcpToolsFromAssembly(assembly)`.
- A primitive without `[McpScope]` is not exposed on any registered scope. Names must be unique within a scope; a duplicate is reported as an error in the log at startup.
- `[Authorize]` hides the tool from `tools/list` and refuses `tools/call` for users without the permission, before the tool runs.
- Tool methods can take any registered service as a parameter, as well as `RequestContext<CallToolRequestParams>` and `CancellationToken`. `HttpContext.GetMcpScope()` returns the scope of the current request.
- In a scope open to anonymous users (`RequireAuthenticatedUser = false`), mark a tool that needs a signed-in user with `[McpRequiresUser]`: an anonymous call to it is answered with an OAuth challenge, so the client signs the user in and repeats the call.
- A tool that returns a list takes an optional `take` and passes it through `IMcpPageSizeResolver.GetPageSizeAsync(take)`, so an agent cannot load thousands of rows into its context with one call.

### Add a scope

A module can register its own scope, or replace the description of a built-in one (the last registration of a name wins):

```csharp
serviceCollection.AddMcpScope(new McpScope
{
    Name = "category-manager",
    Title = "Category Manager",
    Instructions = "Tools for category managers...",
    RequireAuthenticatedUser = true,
    Permissions = ["catalog:access"],
    ApprovalPermission = "catalog:approve", // optional: turns approval on in the scope
});
```

`Version` sets the server version reported to MCP clients; it defaults to the version of this module. With `ApprovalPermission`, the review tools appear in the scope and work with its changes only; to narrow what an approver reviews, for example to the changes of their own seller, override `McpApprovalService.CanReviewAsync`.

### Permissions limited to stores or sellers

`[Authorize("marketing:read")]` accepts a permission granted globally only: a user whose role grants `marketing:read` for some stores (a permission scope, such as `MarketingStoreSelectedScope`) would not see the tool at all. Use `[McpPermission]` instead: the tool is visible to users who have the permission globally or with scopes, and the tool limits the data itself.

```csharp
[McpServerTool(Name = "search_promotions", ReadOnly = true)]
[McpPermission("marketing:read")]
public object SearchPromotions(RequestContext<CallToolRequestParams> context, string store_id = null)
{
    var grant = context.User.GetMcpPermissionGrant("marketing:read");
    var storeIds = grant.IsGlobal ? null : grant.GetScopeValues("MarketingStoreSelectedScope"); // null: every store

    if (store_id != null && !grant.Allows("MarketingStoreSelectedScope", store_id))
    {
        throw new McpForbiddenException($"You do not manage the marketing of store '{store_id}'.");
    }
    ...
}
```

- `GetMcpPermissionGrant` reads the scopes as type and value, so the tool does not depend on the scope classes of the module that defines them. A scoped claim that cannot be read grants no scope, never the global permission.
- `McpForbiddenException` returns the message to the agent as the tool's error, and the call is audited as `Forbidden`.
- `[McpPermission]` and `[Authorize]` can be combined; the user needs both.

> [!NOTE]
> The platform role manager keeps permission scopes only when a role is updated: a role created with scoped permissions in one call is saved with global permissions. Save the role once more after creating it, until this is fixed in the platform.

### Design a toolset

Give each role a toolset with five layers, in roughly this ratio:

| Layer | Count | What it is | The module helps with |
| --- | --- | --- | --- |
| Orientation | 2-3 | "Where am I, what is the state": aggregated, opinionated reads that the model can reason over | |
| Finders | 2-4 | Scoped searches returning ids and the 5-8 fields that matter for the decision, never full objects | `IMcpPageSizeResolver` for the page size |
| Analysis | 1-3 | Answers the platform computes cheaply and the model cannot: what is over budget, what stacks, what is missing | |
| Intent-level actions | 3-6 | The verbs of the role, not entity saves: `pause_promotion`, `hold_order_for_review`. Each takes `dryRun` and returns a diff. | `McpChangePreview` for the diff, `[McpRequiresApproval]` for a second person |
| Bulk | 1-2 | Accept a list or a filter, start a background job, return a job handle and a preview of the first items | `IMcpJobDispatcher` and `get_job_status` |

### Previews

`McpChangePreview` is a common shape for what an action does or did: the action, the object, a list of field changes (`from`, `to`) and warnings for the approver. Return it from the dry run and from the real run, so the agent, the approver and the audit see the same thing; the **Agent approvals** screen renders it as a diff.

```csharp
var preview = new McpChangePreview { Action = "extend", ObjectType = "promotion", ObjectId = promotion.Id, DryRun = dryRun }
    .AddChange("end", promotion.EndDate, newEndDate)
    .AddWarning("Estimated extra discount cost: about 30,000 USD.");
```

### Approval conditions

When only some calls of a tool are risky, name a condition: a public static method of the tool class that decides from the arguments.

```csharp
[McpRequiresApproval(Condition = nameof(NeedsApproval))]
public Task<object> GenerateCoupons(string promotion_id, int count, bool dryRun = false) { ... }

// One code for a complaint runs at once; a mass mailing waits for a second person.
public static bool NeedsApproval(IReadOnlyDictionary<string, JsonElement> arguments) =>
    !arguments.TryGetValue("count", out var count) || count.GetInt32() > 1000;
```

A condition that cannot be found counts as "needs approval" and is reported as an error in the log at startup, so a typo never skips the approval.

### Bulk tools and jobs

A bulk tool hands the work to the platform background job engine through `IMcpJobDispatcher` and returns at once:

```csharp
return await jobDispatcher.EnqueueAsync<GenerateCouponsJob>(requestContext, payload, new McpJobRequest
{
    Title = $"Generate {count} coupons",
    TotalCount = count,
    Preview = firstCodes,
});
```

- The handler is an ordinary `IBackgroundJobHandler<TPayload>`, registered with `AddBackgroundJob`. Its progress reports become the progress of the job, shown in the admin UI and returned by `get_job_status`.
- The result is an `McpJobHandle`: `job_id`, `status`, `title`, `total_count` and the preview. A queued job is canceled at once by `cancel_job`; a running one stops when the job engine supports cancellation.
- A job is visible to the user who ran the tool and, when an approved change started it, to the user who requested the change; in the `admin` scope approvers see every job.

### Sensitive arguments

Mark the arguments that carry personal data with `[McpSensitive]`, on a tool method parameter or on a property of an argument type, nested types and collections included:

```csharp
public Task<object> UpdateContact(string contactId, [McpSensitive] string email, ContactInput contact) { ... }

public class ContactInput
{
    [McpSensitive]
    public string Phone { get; set; }
}
```

Their values are replaced with `***` in the audit log, in `get_pending_change` and in the REST API. A change waiting for approval keeps the values, because the tool needs them when the change is approved; they are replaced as soon as the change is applied, fails, is rejected or expires. The dry-run preview and the result of a tool are stored as the tool returns them, so a tool should not return sensitive values in either.

### Where to put the MCP code of a module

Keep everything a module exposes through MCP in a project of its own, `VirtoCommerce.<Module>.MCP`, next to `Core`, `Data` and `Web`. The MCP surface of the module is then visible at a glance, and only this project depends on the MCP SDK.

```
src/
  VirtoCommerce.Marketing.Core/
  VirtoCommerce.Marketing.Data/
  VirtoCommerce.Marketing.MCP/      tools, prompts, resources; references VirtoCommerce.MCP.Core
    Tools/PromotionTools.cs
  VirtoCommerce.Marketing.Web/      Module.cs references the MCP project
```

- Tools stay thin: they validate arguments and call the module's services, which keep the business logic. A service replaced through DI changes the tool too.
- Register the project with one line in `Module.Initialize`: `serviceCollection.AddMcpToolsFromAssembly(typeof(PromotionTools).Assembly)`, and the same for prompts and resources.
- Declare the dependency on `VirtoCommerce.MCP` in `module.manifest`.

## How to extend

### Extend another module's toolset without changing it

A partner project changes the behaviour of a module's tools with the platform's usual extension points, in a module of its own that depends on the extended one:

| Need | Extension point |
| --- | --- |
| A new field on a model | `AbstractTypeFactory<T>.OverrideType<T, TDerived>()` |
| Company rules in existing tools | Register a derived service in place of the original one; the tools resolve it from DI |
| New tools, prompts and resources next to the existing ones | `AddMcpToolsFromAssembly`, `AddMcpPromptsFromAssembly`, `AddMcpResourcesFromAssembly` with the same `[McpScope]` |
| Settings of the extension | Options bound from configuration |
| A tool that must not be used | **Agent tools** or `MCP.Tools.Disabled` |

- Declare the extended module as a dependency in `module.manifest`, so the platform initializes your module after it and your registrations win.
- A tool name is unique in a scope: add tools under new names, and change the behaviour of an existing tool through the services it uses.
- To be extensible this way, a module creates its models with `AbstractTypeFactory`, registers its services in DI, and makes their methods `virtual`.

The [extension sample](samples/VirtoCommerce.MCP.MarketingExtensionSample/README.md) applies each of these to the marketing sample, with unit tests.

### Replace the MCP infrastructure

The behaviour of this module can be replaced through DI:

- `IMcpPublicOriginResolver` decides the public origin of a scope, for example from a store URL instead of configuration.
- `McpChallengeWriter` writes the HTTP 401 and 403 responses of the MCP endpoints; a module with an error format of its own replaces the bodies and keeps the status codes and the `WWW-Authenticate` header.
- `IMcpSessionService` checks that a token is still valid and signs the user out.
- `IMcpJobDispatcher` starts and tracks the jobs of bulk tools.
- `IMcpPageSizeResolver` decides how many rows a list tool returns.
- `IMcpApprovalService` implements the pending change lifecycle; `McpApprovalService` has virtual methods for the self-approval and expiration rules, and `CanReviewAsync` for which changes an approver may review.
- `IMcpCleanupService` decides what the cleanup deletes.
- `McpPendingChangeReviewService`, `McpCallToolFilter`, `McpPendingChangeExecutor`, `McpAuditWriter`, `McpToolAuthorizationService`, `McpToolAvailabilityService` and `McpTokenValidator` have virtual methods for the back-office review, the execution pipeline, the execution of an approved change, the audit entry, the tool permission check, the tool switches and the token check.

## Components

### Projects

| Project | Layer | Purpose |
| --- | --- | --- |
| `VirtoCommerce.MCP.Core` | Core | Models, service interfaces, attributes (`[McpScope]`, `[McpPermission]`, `[McpRequiresApproval]`, `[McpSensitive]`), registration extensions, diagnostics. The only package a module with tools references. |
| `VirtoCommerce.MCP.Data` | Data | Approval, audit and job services, EF Core repository, cleanup |
| `VirtoCommerce.MCP.Data.SqlServer` | Data | SQL Server migrations |
| `VirtoCommerce.MCP.Data.MySql` | Data | MySQL migrations |
| `VirtoCommerce.MCP.Data.PostgreSql` | Data | PostgreSQL migrations |
| `VirtoCommerce.MCP.Web` | Web | MCP endpoints, authentication middleware, execution pipeline, review and job tools, REST API, module bootstrapping; the Admin Shell plugin in `vue/` |
| `VirtoCommerce.MCP.Tests` | Tests | Unit and endpoint tests |

### Key Services

| Service | Interface | Responsibility |
| --- | --- | --- |
| `McpScopeRegistry` | `IMcpScopeRegistry` | Scopes registered by modules and configuration; finds the scope of a request by path or host |
| `McpCallToolFilter` | | The execution pipeline of every `tools/call`: switches, permissions, arguments, approval, audit, telemetry |
| `McpApprovalService` | `IMcpApprovalService` | Pending change lifecycle: submit, approve, complete, reject, expire |
| `McpPendingChangeExecutor` | | Runs an approved change as the approver, in an MCP session or from the back office |
| `McpPendingChangeReviewService` | | Back-office review with the same rules as the review tools |
| `McpAuditWriter` | | Audit entries with masked arguments |
| `McpJobDispatcher` | `IMcpJobDispatcher` | Background jobs of bulk tools and their tracking |
| `McpToolAuthorizationService` | | `[Authorize]`, `[McpPermission]` and approver checks |
| `McpToolAvailabilityService` | | Tool switches (`MCP.Tools.Disabled`) |
| `McpCleanupService` | `IMcpCleanupService` | Retention of audit entries, changes and jobs |
| `McpPublicOriginResolver` | `IMcpPublicOriginResolver` | Public origin and OAuth resource of a scope |
| `McpSessionService` | `IMcpSessionService` | Token validity and sign-out |
| `McpConnectedAgentService` | | Agents connected to MCP endpoints (OAuth clients with an MCP resource) and their disconnection |

### REST API

Base route: `api/mcp`

| Method | Endpoint | Permission | Description |
| --- | --- | --- | --- |
| `GET` | `/scopes` | signed in | Scopes, with whether the user approves in each |
| `GET` | `/summary` | signed in | Changes waiting for the user, failed calls of the last day, switched-off tools |
| `POST` | `/pending-changes/search` | approver of the searched scopes | Search submitted changes |
| `GET` | `/pending-changes/{id}` | approver of the change's scope | A change with its arguments, preview and result |
| `POST` | `/pending-changes/{id}/approve` | approver of the change's scope, allowed to run the tool | Approve and run a change |
| `POST` | `/pending-changes/{id}/reject` | approver of the change's scope | Reject a change |
| `POST` | `/audit/search` | `mcp:audit:read` | Search the audit log, by tool, outcome, scope, user or trace id |
| `GET` | `/tools` | `mcp:tools:manage` | The tools of every scope and whether they are switched on |
| `PUT` | `/tools/availability` | `mcp:tools:manage` | Switch a tool, or a scope with `*`, on or off |
| `POST` | `/connected-agents/search` | signed in; everyone's with `mcp:agents:manage` | Agents users signed in to, with their client, endpoint and activity |
| `DELETE` | `/connected-agents/{id}` | the agent's user, or `mcp:agents:manage` | Disconnect an agent: revoke its authorization and tokens |

MCP endpoints: `POST /{scope}/mcp`, `POST /mcp` on a host of a scope, and the protected resource metadata at `GET /.well-known/oauth-protected-resource/{scope}/mcp`.

## Samples

| Sample | What it shows |
| --- | --- |
| [Sample module](samples/VirtoCommerce.MCP.SampleModule/README.md) | The smallest module with tools: a permission, approval with a dry run, a sensitive argument |
| [Marketing sample](samples/VirtoCommerce.MCP.MarketingSample/README.md) | A complete toolset for a marketer, in the five layers of [Design a toolset](#design-a-toolset), on realistic mock data |
| [Marketing extension sample](samples/VirtoCommerce.MCP.MarketingExtensionSample/README.md) | A partner module that adds its own rules and tools to the marketing toolset without changing it, with unit tests |

Do not install the samples on a shared or production environment.

## Documentation

* [Demo: an AI agent in the back office](docs/demo.md): Claude Code signs in to a local platform, calls tools, and a second user approves its change
* [Marketing with an AI agent](docs/marketing-demo.md): a marketer's week with the marketing sample
* [Moving UCP to the MCP module](docs/ucp-migration.md)
* [VCST-6155: what is covered](docs/vcst-6155-coverage.md)
* [View on GitHub](https://github.com/VirtoCommerce/vc-module-mcp/)

## References

* [Model Context Protocol specification](https://modelcontextprotocol.io/specification)
* [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
* [Deployment](https://docs.virtocommerce.org/platform/developer-guide/Tutorials-and-How-tos/Tutorials/deploy-module-from-source-code/)
* [Installation](https://docs.virtocommerce.org/platform/user-guide/modules-installation/)
* [Home](https://virtocommerce.com)
* [Community](https://www.virtocommerce.org)
* [Download latest release](https://github.com/VirtoCommerce/vc-module-mcp/releases/latest)

## License

Copyright (c) Virto Solutions LTD.  All rights reserved.

Licensed under the Virto Commerce Open Software License (the "License"); you
may not use this file except in compliance with the License. You may
obtain a copy of the License at

<https://virtocommerce.com/open-source-license>

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
implied.
