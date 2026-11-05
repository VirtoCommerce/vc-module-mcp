# Model Context Protocol (MCP) Module

## Overview

The MCP module connects AI agents such as Claude, ChatGPT or your own agent to the Virto Commerce platform through the [Model Context Protocol](https://modelcontextprotocol.io). It is the platform-level MCP infrastructure: it hosts the MCP endpoints, signs users in with the platform's own OAuth, checks permissions, runs every tool call through a common pipeline, holds risky changes for a second person's approval, and audits every call. Feature modules add tools, prompts and resources declaratively and keep to their business logic.

An agent works as the signed-in user: it sees only the tools that user may use, limited to the user's stores, and a change it asks for can wait in the back office until an approver applies it. Built on the official C# SDK, [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore).

**Who reads what.** Business users: [Connect an AI agent](#connect-an-ai-agent). Administrators: [Set up the platform for agents](#set-up-the-platform-for-agents). Approvers and administrators: [Work with agents in the back office](#work-with-agents-in-the-back-office). System integrators and developers: [Configuration](#configuration) and the sections after it.

## Key Features

* **Scopes**: one MCP endpoint per kind of agent (`admin`, `vendor`, a buyer scope, your own), at `/{scope}/mcp` or at `/mcp` of a host of its own
* **Platform sign-in**: OAuth 2.1 with PKCE and a token issued for one endpoint (RFC 8707), which the MCP endpoints check; anonymous scopes with sign-in on demand per tool; revoked sessions stop working on the next request. The rest of the platform does not check the token's audience yet: see [What the token protects today](#what-the-token-protects-today)
* **Permissions**: `[Authorize]` for platform permissions, `[McpPermission]` for permissions limited to stores or sellers; tools the user may not use are not listed
* **Confirmation in the chat**: `[McpRequiresConfirmation]` asks the user to confirm a risky call in the agent's chat before it runs (MCP elicitation), with clients that support it
* **Approval with a diff**: `[McpRequiresApproval]` holds a call as a pending change with a dry-run preview until a second person approves it, always or only when a condition on the arguments says so; the requester's access is checked again when it is approved
* **Audit**: every call with user, client, arguments, outcome, duration and trace id; `[McpSensitive]` masks personal data
* **Bulk jobs**: a bulk tool hands its work to a platform background job; the agent follows and cancels it
* **Kill switch**: switch a tool, or a whole scope, off without a release
* **Back office**: Admin Shell screens for the approval queue, the audit log, the tool switches and the connected agents, for desktop and phone
* **Observability**: OpenTelemetry traces, metrics and logs of tool calls, reviews and jobs
* **Guard rails for agents**: a row limit for list tools, errors the model can correct, a common result shape for previews
* **Multi-database support**: SQL Server, MySQL and PostgreSQL

Not in the module, by design: rate limits and CORS, which belong to the infrastructure in front of the platform (see [Rate limiting and CORS](#rate-limiting-and-cors)).

## Connect an AI agent

> [!NOTE]
> The steps below are an example, with the back-office endpoint `/admin/mcp`. The MCP module is the infrastructure: on its own it serves only its approval and job tools. The tools an agent works with, and every scope besides `admin` and `vendor`, are registered by other modules. See [UCP](https://github.com/VirtoCommerce/vc-module-ucp) for a buyer scope, `/ucp/mcp`, with its shopping tools, and the [Samples](#samples) for back-office tools (marketing, content); to write your own, see the [Developer guide](#developer-guide).

You need two things from your administrator: the **endpoint URL** and the agent's **client id**.

```text
https://virtostart-demo-admin.govirto.com/admin/mcp
```

The endpoint is your platform's public HTTPS address followed by `/admin/mcp`; replace the demo address above with yours. If you are the administrator and nobody has set the platform up yet, do [Set up the platform for agents](#set-up-the-platform-for-agents) first, once.

Then pick your agent:

**Claude Code**

```bash
claude mcp add --transport http --client-id claude-code --callback-port 65111 virto https://virtostart-demo-admin.govirto.com/admin/mcp
```

In Claude Code, run `/mcp`, select `virto`, choose **Authenticate** and sign in. Add `--scope project` to share the server with your team through the folder's `.mcp.json`; the token stays in your profile.

**Claude Desktop and claude.ai**

1. **Settings → Connectors → Add custom connector.**
1. Name: `Virto Commerce`. URL: the endpoint. Under **Advanced settings**, OAuth client id: the one your administrator gave you.
1. **Connect**, sign in and allow access.

**ChatGPT**

In developer mode (ChatGPT Business, Enterprise or Edu), **Settings → Apps → Create** an app with the endpoint URL, authentication **OAuth** and the client id, then sign in and allow access. See OpenAI's [developer mode guide](https://help.openai.com/en/articles/12584461-developer-mode-apps-and-full-mcp-connectors-in-chatgpt-beta).

**VS Code**

Add the server to `.vscode/mcp.json`, then choose **Start** above it; VS Code asks for the client id and opens the sign-in:

```json
{
  "servers": {
    "virto": { "type": "http", "url": "https://virtostart-demo-admin.govirto.com/admin/mcp" }
  }
}
```

**Other agents.** Any MCP client with the streamable HTTP transport and OAuth with a client id works the same way: give it the endpoint and the client id.

**Try it.** The agent works as you: it sees only the tools you may use, in your stores. Ask for example:

* *What can you do in Virto Commerce?*
* *Which promotions end this week, and how do they perform?*
* *Pause the "Summer sale" promotion.* When a tool needs approval, the change waits in **Agent approvals** until an approver applies it, and the agent gives you its change id.

Menu names in the agents change from release to release; if a step does not match, check the agent's own documentation. If the sign-in fails, see [Troubleshooting](#troubleshooting).

## Set up the platform for agents

An administrator does this once per platform, and registers one OAuth client per kind of agent.

1. **Install** `VirtoCommerce.MCP` (platform 3.1091.0 or later) and the modules that register the tools and scopes, such as UCP or your own (see the [Samples](#samples)). Bulk tools also need `VirtoCommerce.BackgroundJobs`; without it, a bulk tool answers that background jobs are not available.
1. **Allow tokens for the endpoint** in `appsettings.json`:

    ```json
    "Authorization": {
      "Resources": [ "https://virtostart-demo-admin.govirto.com/admin/mcp" ]
    }
    ```

1. **Register the agent** as an OAuth client with `POST /api/platform/oauthapps` (Swagger at `/docs`). Change only `clientId`, `displayName` and `redirectUris` from one agent to the next:

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

    | Agent | Redirect URI |
    | --- | --- |
    | Claude Code | `http://localhost:65111/callback` (the port of `--callback-port`) |
    | Claude Desktop, claude.ai | `https://claude.ai/api/mcp/auth_callback` |
    | ChatGPT | `https://chatgpt.com/connector_platform_oauth_redirect` |
    | VS Code | `http://127.0.0.1:33418` and `https://vscode.dev/redirect` |

    The redirect URI is where the agent receives the result of the sign-in, not an address of the platform. The values are the ones the agents use at the time of writing; check the agent's documentation if a sign-in ends with an error page.

1. **Grant permissions** with roles. Agents need no permission of their own: an agent sees the tools whose permissions the user has, the same ones that protect the back office and the REST API (for example `marketing:read`, limited to the user's stores). Give `mcp:approve` to the people who approve agents' changes, and `mcp:audit:read` and `mcp:manage` to the people who run the agents.
1. **Hand out** the endpoint URL and the client id of each agent.

> [!NOTE]
> Two stories make this shorter. With [VCST-6641](https://virtocommerce.atlassian.net/browse/VCST-6641), the OAuth client is set up in **Security → OAuth applications** of the back office instead of the API. With [VCST-6639](https://virtocommerce.atlassian.net/browse/VCST-6639), agents that identify themselves with a client ID metadata document connect with the endpoint URL alone, as with Shopify, BigCommerce or commercetools.

<details>
<summary>Why each field of the client</summary>

* `clientType: public`: the agent holds no secret, only the user's token.
* `requirements: ft:pkce`: the sign-in is protected by PKCE, as the MCP specification requires.
* `consentType: explicit`: the user sees which agent they let act as them, once per user and agent.
* `permissions: rsrc:…`: the resources the client may ask a token for (RFC 8707); it must match the URL the agent uses exactly, scheme and port included. It limits what a client may request, not what it receives: a token request that names no resource gets a token for the whole platform API (`resource_server`). MCP clients name the MCP endpoint, so their tokens are for that endpoint only; see [What the token protects today](#what-the-token-protects-today).
* `POST /api/platform/oauthapps` also adds default permissions to every client (`client_credentials`, `email`, `profile`, end session). They give a public client no access, since it holds no secret.

</details>

### Troubleshooting

| Symptom | Cause |
| --- | --- |
| The sign-in page does not open, or the sign-in ends with an error page | The redirect URI or the callback port of the client does not match its registration |
| `invalid_token` after the sign-in | The token was issued for another address: the URL the agent uses must be listed in `Authorization:Resources` and in the client's `rsrc:` permission exactly, scheme and port included |
| HTTP 403 | The scope requires a permission the user lacks, for example `vendor_portal:access` for `/vendor/mcp` |
| The agent sees fewer tools than expected | Tools the user has no permission for are not listed; a tool switched off in **Agent tools** is not listed either |
| claude.ai or ChatGPT cannot reach the endpoint | They connect from their own cloud: the endpoint must be reachable from the internet over HTTPS |

To check a connection, ask the agent "What can you do in Virto Commerce?": **Configuration → AI agents (MCP) → Agent audit log** shows the call with the user and the client id, and **Connected agents** lists the agent. [Demo: an AI agent in the back office](docs/demo.md) walks through all of this on a local platform with Claude Code.

## Work with agents in the back office

Everything people do with agents is in the back office, under **Configuration → AI agents (MCP)**, on desktop and phone:

| Screen | Who | What it does |
| --- | --- | --- |
| Agent approvals | approvers of a scope (`mcp:approve`, or the scope's `ApprovalPermission`) | The queue of changes agents asked to make, with a menu counter. A change opens with **what will change**: the object, a *now → after* diff and the warnings of its dry run, then the request and the arguments. **Approve** runs the tool right away as the reviewer, with the same rules as `approve_pending_change`; **Reject** records the reason. Once applied, the screen shows the tool's result as a diff. |
| Agent audit log | `mcp:audit:read` | Every tool call with the user, the client, the scope, the duration and the outcome, filtered by outcome and scope. The search box takes a tool name, or the trace id an agent reported. A call links to the change it submitted or executed. |
| Agent tools | `mcp:manage` | Every tool of every scope with its kind (read, write, needs approval) and a switch; one switch per scope turns a whole scope off. |
| Connected agents | every signed-in user for their own agents, `mcp:manage` for everyone's | The agents users signed in to: the OAuth client, the user, the MCP endpoint, when it was connected and last used, and its active sign-ins. **Disconnect** revokes the sign-in at once. |

The global search of the back office (**Ctrl+K**) finds them too: **Agent tools** by name, title or description, with an *Off* or *Approval* mark (`mcp:manage`), and **Connected agents** by client or user name (one's own, or everyone's with `mcp:manage`). A result opens its screen with the search filled in; type `tools:` or `agents:` to search only there.

### Approve or reject a change

A tool marked as risky does not change anything when an agent calls it: the agent submits the change with a dry run and gives the user the change id. An approver opens it in **Agent approvals**, reads what will change and the warnings of the dry run, and approves or rejects it with a comment. **Approve** first checks that the requester may still make the change (the tool's dry run is repeated as the requester), then runs the tool right away, as the approver. By default nobody approves their own change, and a change that nobody reviews expires after 72 hours; [Approval](#approval) has all the rules.

### Find a call in the audit log

**Agent audit log** lists every tool call with the user, the agent's OAuth client, the scope, the duration and the outcome. When a user reports a problem, ask for the trace id the agent shows with an error and paste it in the search box: it finds the call, and the same id leads to the traces in your monitoring.

### Switch a tool off

An administrator switches tools off without a release on the **Agent tools** screen, or with the `MCP.Tools.Disabled` setting, one entry per line:

| Entry | Switches off |
| --- | --- |
| `activate_promotion` | the tool in every scope |
| `admin/activate_promotion` | the tool in the `admin` scope only |
| `vendor/*` | every tool of the `vendor` scope |

A tool that is switched off disappears from `tools/list`, a call to it is refused with a message the agent can pass on to the user and audited as `Disabled`, and its pending changes cannot be approved until it is switched on again. The change applies to the next request, with no restart.

### Disconnect an agent

In **Connected agents**, users see the agents they signed in to, with when they were last used. **Disconnect** ends an agent's sign-in at once: its next request is refused, and the user signs in again to reconnect. With `mcp:manage`, an administrator sees and disconnects everyone's agents.

## Configuration

> [!NOTE]
> This section and the ones after it are for system integrators and developers.

### appsettings.json

```json
"MCP": {
  "PublicOrigin": "https://admin.example.com",
  "MaxRequestBodySize": 1048576,
  "ValidateAgentTokensOnAllEndpoints": true,
  "Scopes": {
    "vendor": { "Enabled": false },
    "ucp": { "Hosts": [ "store.example.com" ] }
  }
}
```

| Key | Description |
| --- | --- |
| `MCP:PublicOrigin` | Origin advertised in the OAuth metadata and expected in token audiences. **Required outside Development**: without it, or the scope's own origin, `/{scope}/mcp` refuses access tokens and the module logs an error at startup, because the origin would otherwise come from the request's `Host` and forwarded headers. In Development it defaults to the origin of the request. |
| `MCP:MaxRequestBodySize` | Largest MCP request body in bytes, `1048576` (1 MB) by default; a larger request is answered with `413` before it is read. `0` turns the limit off. The platform itself sets no limit, and tool arguments are small JSON. |
| `MCP:ValidateAgentTokensOnAllEndpoints` | `true` by default: on every endpoint of the platform, an agent's token is looked up in the token store, so **Disconnect** ends an agent's access to the REST API and GraphQL at once, not only to MCP: a request with a revoked agent token gets `401 invalid_token`. An agent's token is one issued for an MCP endpoint (its audience, of any scope, switched off or not), or issued to a client application registered for an MCP resource, as **Connected agents** lists them; the second covers a token of the agent's authorization requested without a resource. Other tokens, cookies and API keys are not looked up, so the rest of the platform behaves as before. It costs one token store lookup per request with an agent's token, and one cached client lookup per client. |
| `MCP:Scopes:{scope}:Enabled` | Switches a scope off. Defaults to `true`. A scope switched off has no endpoint and no metadata, and is not on **Agent tools**; `{scope}/*` in `MCP.Tools.Disabled` instead keeps the endpoint and refuses every call. |
| `MCP:Scopes:{scope}:PublicOrigin` | Origin of one scope, for example the storefront host for a buyer scope. A module can decide the origin of its own scope instead: the UCP module takes it from the store or `UCP:PublicOrigin`. |
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
| `mcp:approve` | Review, approve and reject changes submitted in the `admin` scope. Other scopes name their own approver permission. |
| `mcp:audit:read` | Read the tool call audit log |
| `mcp:manage` | See the tools of every scope and switch them on and off; see and disconnect the AI agents of every user (without it, users see and disconnect their own) |

There is no permission to connect an agent. A tool is protected by the permission of what it does, the same one the back office and the REST API check, so an agent can do what its user can do and no more. Who may connect at all is decided by the OAuth clients an administrator registers, the user's consent, **Disconnect** in **Connected agents** and the scope switch in **Agent tools**. An MCP endpoint accepts only a token issued to a client: a token with no client, which the platform's password grant issues to an anonymous client, skips the consent and could not be disconnected, so it is refused. A scope can still require a permission for every request (`McpScope.Permissions`), as `vendor` requires `vendor_portal:access`.

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
| Span per back-office review | `MCP review {decision} {tool}`, decision `approve` or `reject` | `vc.mcp.scope`, `gen_ai.tool.name`, `vc.mcp.pending_change_id`, `vc.mcp.approval.decision`, `vc.mcp.review.channel`; the spans the tool creates are inside it |
| Counter | `vc.mcp.tool.calls` | scope, tool, outcome |
| Histogram, seconds | `vc.mcp.tool.duration` | scope, tool, outcome |
| Counter | `vc.mcp.approval.decisions` | scope, tool, decision (`submitted`, `approved`, `applied`, `failed`, `rejected`, `expired`) |
| Counter | `vc.mcp.jobs` | final status |
| Log | `MCP tool {ToolName} called in scope {ScopeName} by {UserName}: {Outcome} in {DurationMs} ms` | with the trace and span ids |

An approved change that runs is recorded in the audit log and in `vc.mcp.approval.decisions`, not as an `MCP tool` span or in the tool call metrics.

Each scope has its own route, so the requests are named after it in traces and Application Insights: `POST /admin/mcp`, `POST /vendor/mcp`. `Experimental.ModelContextProtocol` adds the SDK's own spans and metrics for the JSON-RPC requests (`tools/call {tool}`, `mcp.server.operation.duration`). With the platform's ASP.NET Core and EF Core instrumentation, one tool call is one trace: the HTTP request, the SDK span, the tool span and the database queries inside it. The trace id is returned to the agent in `_meta.trace_id` and stored on the audit entry, so a user's report, the audit log and the traces lead to each other.

The SDK copies the text of a tool's error result into the status description of its `tools/call` span. That text can carry a caller's data, so the module keeps the span's `Error` status but replaces the description with the fixed `The tool returned an error.`; the agent still receives the full error, and the audit entry keeps it behind its permission.

### Rate limiting and CORS

The module does not limit the request rate and does not add CORS headers: both belong to the infrastructure in front of the platform. Limit the rate of requests to `/*/mcp`, and to `/mcp` on the hosts of scopes, in the API gateway or the load balancer, because an agent that goes into a loop can call a tool hundreds of times a minute. CORS is needed only for MCP clients that run in a browser; Claude, ChatGPT and other hosted clients call the server from their own back end.

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

### Request pipeline

The MCP endpoints are endpoints of the platform's request pipeline, like its controllers, so an MCP request goes through the platform's error handling, security headers, forwarded headers, authentication and account lockout. The scope of a request is the metadata of the endpoint that routing matched (`McpScopeEndpointMetadata`), and the module adds its steps through the platform's module hooks:

| Where | Hook | What runs |
| --- | --- | --- |
| After routing, before authentication | `IPlatformStartup.ConfigureAfterRouting` | The body size limit (`413` before anything reads the body), then the compatibility of JSON-only `tools/list` probes |
| After authentication, before authorization | `IPlatformStartup.ConfigureAfterAuthentication` | The scope's token rules: the user comes from the MCP access token only; the `401` challenge and `403` of the MCP authorization flow. On the other endpoints, an agent's token is looked up in the token store (`MCP:ValidateAgentTokensOnAllEndpoints`) |
| With the platform's endpoints | `IModule.PostInitialize` | `UseEndpoints(e => e.MapMcpScopes())`: `/{scope}/mcp` of every scope, and `/mcp` of a scope's hosts (`RequireHost`) |

The MCP steps run only for a request whose endpoint is an MCP endpoint; other requests pass them untouched, except for the token store lookup of an agent's token. A module that adds a step for its own scope, such as UCP's store of the request, registers it in the same hooks and finds the scope with `HttpContext.GetMcpScope()`.

### Tool call flow

1. The agent sends `tools/call` to a scope's endpoint with the user's bearer token.
1. The module resolves the scope from the path or the host, reads the token with the platform's OpenIddict validation, and checks that it was issued for this endpoint, to a user (not to the client itself), and is still valid in the platform token store.
1. The scope's permission, if it has one, and the tool's `[Authorize]` permissions are checked, then the tool switch (`MCP.Tools.Disabled`), then `[McpPermission]` and, for review tools, whether the user is an approver.
1. Required arguments are validated. A missing argument is returned as the tool's error, not a protocol error, so the model can correct the call.
1. A tool marked `[McpRequiresConfirmation]` asks the user in the chat first, when the client can ask (see [Confirmation in the chat](#confirmation-in-the-chat)); a call the user declines does not run.
1. A tool marked `[McpRequiresApproval]` does not run: it runs once with its `dryRun` (or `dry_run`) parameter set, and the call is stored as a pending change with that preview. If the dry run fails, nothing is submitted and the agent gets the error. A tool that requires approval but has no dry-run parameter is refused, and reported as an error at startup. With a `Condition`, only the calls the condition selects are held.
1. Otherwise the tool runs as the user and calls the module's services.
1. The call is audited (user, OAuth client, tool, arguments, outcome, duration, trace id), logged and traced.
1. An approver reviews the pending change in the back office or with the review tools. Approving repeats the dry run as the requester, and then runs the tool with the stored arguments, as the approver. The scope's call filters run around both, as around the call that submitted it.

A tool refuses a request with `McpException` (audited as `Failed`) or `McpForbiddenException` (audited as `Forbidden`): the agent gets the message as it is, as the tool's error. A call that returns a tool error carries the trace id in `_meta.trace_id`; protocol errors, such as an unknown tool, are JSON-RPC errors without it.

### Scopes

A scope is an MCP surface for one kind of agent. Only the tools, prompts and resources marked for a scope are visible and callable on it.

| Scope | Endpoint | Registered by | Who connects |
| --- | --- | --- | --- |
| `admin` | `/admin/mcp` | this module | signed-in platform users; each sees the tools their permissions allow |
| `vendor` | `/vendor/mcp` | this module | vendor portal users with the `vendor_portal:access` permission (defined by the marketplace vendor module; without it the scope refuses everyone) |
| `ucp` | `/ucp/mcp` | the UCP module, from its VCST-6155 version ([Moving UCP to the MCP module](docs/ucp-migration.md)); switching the scope off turns all of UCP off; earlier UCP versions map the endpoint themselves (see [Coexistence](#coexistence-with-modules-that-host-their-own-mcp-endpoint)) | buyers and shopping agents |

Every request is stateless: the scope and the user are resolved per HTTP request, so replicas need no session affinity and one deployment serves all scopes side by side.

A scope can also be served at `/mcp` on hosts of its own, for example the storefront host for a buyer scope, set with `MCP:Scopes:{scope}:Hosts`; the scope stays available at `/{scope}/mcp` as well. At `/mcp` of a host, the OAuth resource is that host's address, so a token for `https://store.example.com/mcp` is not accepted at `https://store.example.com/ucp/mcp`, and the other way round.

### Authentication

MCP endpoints accept only bearer tokens issued by the platform for the exact MCP resource (RFC 8707 audience), read with the platform's OpenIddict validation. Cookies, API keys and basic authentication are ignored on them. A token issued to the client itself (client credentials, whose subject is the client) is refused: every call acts as a signed-in user. An anonymous request to a scope that requires a user gets an HTTP 401 challenge pointing to the scope's protected resource metadata (RFC 9728), which starts the platform OAuth flow in the MCP client:

```
GET /.well-known/oauth-protected-resource/{scope}/mcp
GET /.well-known/oauth-protected-resource/mcp        (the /mcp endpoint of a scope host)
```

On every request the token is also looked up in the platform token store, so a revoked token or sign-in session stops working on all replicas right away. A tool that signs the user out, such as a buyer's `logout`, calls `IMcpSessionService.SignOutAsync(user)`.

A token without a token id is refused while the platform stores tokens, which it does by default.

#### What the token protects today

The MCP endpoints check that a token was issued for them. The rest of the platform does not check token audiences yet, so an access token issued for `/admin/mcp` is also accepted by the platform's REST API and GraphQL, with the permissions of its user. Until the platform checks audiences per endpoint:

- The tool switch, `[McpRequiresApproval]`, `[McpRequiresConfirmation]` and the MCP audit apply to calls made through MCP. They guard the user against an agent's mistakes; they are not a boundary against whoever holds the token, who can call the REST API the tools wrap directly.
- **Disconnect** stops the agent's token at once on MCP and, with `MCP:ValidateAgentTokensOnAllEndpoints` (on by default), on the REST API and GraphQL as well. With it off, the token works on the REST API until it expires. The platform's own `Authorization:EnablePersistentStorageTokenValidation` checks every token instead, which also ends a token as soon as its user's password or roles change.
- A client registered with `rsrc:` permissions that asks for a token without naming a resource gets a token for the whole platform API (`resource_server`). It is still an agent's token: **Disconnect** ends it on the REST API too, and the back office refuses it for reviewing pending changes.

### Approval

- By default a change cannot be approved by the user who requested it (`MCP.Approval.AllowSelfApproval`), nor by another user while the requester is signed in as them (`security:loginOnBehalf`).
- Pending changes are reviewed by a person: `approve_pending_change` runs only after the user confirms it in the chat, and the back office's approve and reject endpoints refuse an agent's token.
- The approver must also be allowed to run the tool, and a tool switched off cannot be approved.
- The requester must still be able to make the change: before it runs as the approver, the tool's dry run is repeated as the requester. A requester whose account is gone or locked, who lost the tool's permission, or whose stores or sellers do not cover the change is refused, and the change stays pending for the approver to reject. A tool that requires approval therefore needs a dry run.
- The change runs as the approver, whose access may be wider. A tool held for approval should store the scope of the change explicitly in its arguments (for example the store ids), not "every store I may use", which differs between the requester and the approver.
- The requester's dry run sees the requester as `RequestContext.User` only; `HttpContext.User` and the platform services that read the current user still see the approver. A tool's dry run should take the user from its request context.
- The dry run runs again at approval, so a dry run must not have side effects (counters, lookups that write).
- A change expires after `MCP.Approval.PendingChangeLifetimeHours` and can no longer be approved.
- A change is executed at most once, even if two approvers, or two replicas, approve it at the same time.
- A call with `dryRun: true` changes nothing, so it runs right away instead of being submitted.
- Changes are approved in scopes with approval only (`McpScope.ApprovalPermission`; `admin` by default). In any other scope, a call to a tool marked `[McpRequiresApproval]` is refused and audited as `Error`, and the tool is reported as an error in the log at startup. So is such a tool without `[McpScope]`, because on endpoints without a scope its calls are not held for approval.
- An approved change runs to the end and its result is recorded, even if the approver's client disconnects.

Changes are reviewed in the back office or with the first four tools below, present in every scope with approval; the two job tools are in the `admin` and `vendor` scopes. An approver of a scope works with every change of that scope; any other user sees and withdraws only the changes they requested.

| Tool | Purpose | Who |
| --- | --- | --- |
| `list_pending_changes` | Compact list of submitted changes, by status | approvers: all changes; others: their own |
| `get_pending_change` | Arguments, dry-run preview, reviewer and result of one change | approvers: any change; others: their own |
| `approve_pending_change` | Approves the change and runs the tool with the stored arguments, as the approver, after the requester's dry run passes; its confirmation in the chat shows the tool, the requester and the masked arguments of the change; with a client that cannot ask, it is refused and the change is approved in the back office | approvers only |
| `reject_pending_change` | Rejects the change; the requester uses it to withdraw their own change | approvers: any change; others: their own |
| `get_job_status` | Status and progress of a background job started by a bulk tool | the user who started it, the requester of the change that started it, approvers |
| `cancel_job` | Cancels a queued or running background job | as `get_job_status` |

### Audit and cleanup

Every call made through a registered scope is audited with one of the outcomes `Succeeded`, `PendingApproval`, `Forbidden`, `Declined` (the user did not confirm it in the chat), `Canceled` (the call was canceled while it ran, by a timeout or a client that went away; it may have changed data partly), `Disabled` (the tool is switched off), `Failed` (the tool returned an error or refused the request with an `McpException`) or `Error` (unknown tool, invalid arguments, a tool that needs approval in a scope without it, unexpected error). Reviews made in the back office are audited with the client id `back-office`. A tool that acts on a pending change links its audit entry to it with `requestContext.SetAuditPendingChangeId(changeId)`.

The recurring job `MCP.Cleanup` deletes audit entries, finished jobs, and applied, failed, rejected and expired changes older than the retention settings. An approved change that never completed is kept: it needs a person to look at it. The job also marks overdue pending changes as expired, which removes their sensitive arguments.

### Admin Shell plugin

The screens of [Work with agents in the back office](#work-with-agents-in-the-back-office) are an [Admin Shell](https://github.com/VirtoCommerce/vc-module-admin-shell) plugin built with VC-Shell. The plugin is in `src/VirtoCommerce.MCP.Web/vue` (Vue 3, `@vc-shell/framework`, Yarn 4). `dotnet publish`, and so `vc-build Compress`, builds it into the package as `plugins/admin-shell`; `-p:SkipAdminShellPlugin=true` leaves it out. To work on it against a running platform:

```bash
cd src/VirtoCommerce.MCP.Web/vue
yarn install
yarn build    # then copy ../plugins/admin-shell to modules/VirtoCommerce.MCP/plugins/admin-shell; no restart needed
```

### Coexistence with modules that host their own MCP endpoint

A module that maps its own MCP endpoint, as earlier UCP versions do at `/ucp/mcp`, shares the same MCP server. On such an endpoint, primitives marked with `[McpScope]` are removed, so scoped tools never appear where scopes are not enforced. [Moving UCP to the MCP module](docs/ucp-migration.md) describes what moves and what stays in UCP. Both modules must use the same version of the MCP SDK (2.3.0 for this module): the platform loads one copy of an assembly, the newest, for all modules.

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
- In a scope that requires a signed-in user, every tool must say who may use it: `[Authorize]`, `[McpPermission]`, `[AllowAnonymous]`, or `[McpAnyUser]` for a tool every signed-in user may call because it checks access to its data itself. A tool that says none of them is not served in that scope, and a warning at startup names it, so a forgotten permission hides the tool instead of opening it to every user. Prompts and resources are not covered by this rule; protect them with `[Authorize]` or `[McpPermission]` when they return data. A prompt or resource whose `[McpPermission]` the user does not have is neither listed nor readable; prompts and resources are not audited and have no switch.
- Tool methods can take any registered service as a parameter, as well as `RequestContext<CallToolRequestParams>` and `CancellationToken`. `HttpContext.GetMcpScope()` returns the scope of the current request.
- In a scope open to anonymous users (`RequireAuthenticatedUser = false`), mark a tool that needs a signed-in user with `[McpRequiresUser]`: it is listed to anonymous users, and an anonymous call to it is answered with an OAuth challenge, so the client signs the user in and repeats the call. A tool with `[Authorize]` or `[McpPermission]` is answered the same way, but is not listed to anonymous users.
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

A scope that needs its own challenge bodies, its own public origin or its own step in the tool calls declares them with the scope; they apply to its requests only, and every other scope keeps the module's:

```csharp
serviceCollection.AddMcpScope(new McpScope { Name = "ucp", RequireAuthenticatedUser = false }, scope => scope
    .WithChallengeWriter<UcpMcpChallengeWriter>()          // McpChallengeWriter: bodies of the 401 and 403 responses
    .WithPublicOriginResolver<UcpMcpPublicOriginResolver>() // IMcpPublicOriginResolver: OAuth resource and token audience
    .WithCallToolFilter<UcpMcpCallToolFilter>());           // IMcpCallToolFilter: a step around every tool call
```

The services are registered under the scope's key and resolved with `IServiceProvider.GetMcpScopeService<T>(scope)`, so an implementation never checks which scope it serves.

`Version` sets the server version reported to MCP clients; it defaults to the version of this module. With `ApprovalPermission`, the review tools appear in the scope and work with its changes only; to narrow what an approver reviews, for example to the changes of their own seller, override `McpApprovalService.CanReviewAsync`.

### Wrap the tool calls of a scope

A module that needs its own step around every tool call of its scope, for example its own error format or its own telemetry, implements `IMcpCallToolFilter` and registers it for the scope:

```csharp
public sealed class StoreToolCallFilter(IStoreService storeService) : IMcpCallToolFilter
{
    public async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        // check or change the call, then call the tool
        return await next(context, cancellationToken);
    }
}

serviceCollection.AddMcpCallToolFilter<StoreToolCallFilter>("category-manager"); // McpScopes.Any for every scope
```

A module that registers the scope itself declares the filter with it instead: `.WithCallToolFilter<StoreToolCallFilter>()` (see [Add a scope](#add-a-scope)).

The filter is resolved from the request's services and runs inside the module's pipeline: after the permission checks, the switch and the argument validation, and within the audit and the `MCP tool` span, so the result it returns is the one that is audited and its spans are children of the tool's span. Calls of other scopes and of endpoints without a scope skip it. For a call held for approval it runs for the dry run; when the change is approved, it runs again around the requester's dry run and around the execution, so a per-tool policy also applies to approved changes. The [UCP module](https://github.com/VirtoCommerce/vc-module-ucp) uses it for its buyer error envelopes and its `UCP {operation}` spans.

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
- `[McpPermission]` only says that the permission is granted somewhere. A tool must narrow the data itself, for every argument that names a store or a seller and for every object it loads by id, as the marketing sample's `MarketingAccess` and `ContentTools` do. A tool that skips it lets a user limited to one store work on all of them.

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
    !arguments.TryGetValue("count", out var count) || count.ValueKind != JsonValueKind.Number || count.GetInt32() > 1000;
```

A condition that cannot be found counts as "needs approval" and is reported as an error in the log at startup, so a typo never skips the approval.

### Confirmation in the chat

A call that changes something at once, without a second person, can still ask its own user first: the agent's client shows the question in the chat with **Confirm** and **Decline**, and the tool runs only after **Confirm**.

```csharp
[McpServerTool(Name = "pause_promotion", Destructive = true)]
[McpRequiresConfirmation("Pause promotion {promotion_id} now? Buyers stop getting its discount right away. Reason: {reason}")]
public object PausePromotion(string promotion_id, string reason, bool dryRun = false) { ... }
```

- `{argument}` in the message is replaced by the value of that argument of the call; `[McpSensitive]` values are shown as `***`. Without a message, the question names the tool and lists its arguments.
- A declined or dismissed question ends the call with a tool error ("The user did not confirm the call, so nothing was done"), and the call is audited as `Declined`. A confirmed call runs and is audited as usual; the question itself is not audited.
- A dry run (`dryRun: true`) does not ask. A tool with both attributes asks before it submits the change for approval.
- The answer is tied to the question: the server issues the question's state protected with ASP.NET Core data protection (signed and encrypted), bound to the user, the tool, the arguments and a nonce, and valid for 10 minutes and once. A client cannot make up a state and attach an answer to its first call; if the agent repeats the call with other arguments, the user is asked again.
- The module's own `approve_pending_change` asks the approver before it applies a change; the marketing sample's `pause_promotion` asks before it pauses a promotion.

**Clients.** The question uses MCP elicitation (form mode). The MCP endpoints are stateless, so a server can't send a client a request of its own in the middle of a call; it answers the call with an input-required result instead, and the client asks the user and repeats the call with the answer: multi round-trip requests (MRTR), from the 2026-07-28 protocol revision. Clients that speak that revision and declare elicitation ask the user. What happens with other clients, today most of them, is the tool's `Fallback`:

| `Fallback` | With a client that cannot ask |
| --- | --- |
| `McpConfirmationFallback.Allow` (default) | The call runs as without the attribute; most clients ask the user to allow each tool call with their own prompt. `pause_promotion` uses it. |
| `McpConfirmationFallback.Refuse` | The call does not run and is audited as `Declined`; the agent is told to have the user do it in the back office. `approve_pending_change` uses it, so a change is approved only where a person confirms it: in the chat of a client that can ask, or in **Agent approvals**. |

```csharp
[McpRequiresConfirmation("Approve change {change_id} and run it now, as you?", Fallback = McpConfirmationFallback.Refuse)]
```

**Not a security control.** The client reports the user's answer, so confirmation guards the user against an agent's mistake, not against a client that answers by itself. For a change that a second person must check, use [approval](#approval).

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
- The result is an `McpJobHandle`: `job_id`, `status`, `title`, `total_count`, `message` and the preview. A queued job is canceled at once by `cancel_job`; a running one stops when the job engine supports cancellation. A queued job of a tool switched off with `MCP.Tools.Disabled` does not start.
- A job is visible to the user who ran the tool and, when an approved change started it, to the user who requested the change; approvers of a scope see every job of that scope.

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

Their values are replaced with `***` in the audit log, in `get_pending_change` and in the REST API. A property is found under the name the serializer gives it (`[JsonPropertyName]`), and a property of the values of a dictionary with string keys is found under every key. A change waiting for approval keeps the values, because the tool needs them when the change is approved; they are replaced in the store as soon as the change is approved, and the approved change runs from the values in memory. The dry-run preview and the result of a tool are stored and shown as the tool returns them, so a tool must not return sensitive values in either.

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

## Package a connector for a solution

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
        ApprovalPermission = ModuleConstants.Security.Permissions.Approve,
    });
    ```

1. **Address.** Serve the endpoint on a public HTTPS host, for example `https://virtostart-demo-admin.govirto.com/admin/mcp`, or give it a host of its own with `MCP:Scopes:admin:Hosts` (`https://mcp.contoso.com/mcp`), and add it to `Authorization:Resources`.
1. **OAuth client for Claude.** Register one client with Claude's callback URL (`https://claude.ai/api/mcp/auth_callback`) and explicit consent, as in [Set up the platform for agents](#set-up-the-platform-for-agents).
1. **Roles.** Grant the client's people the permissions of their tools, limited to their stores; approvers get `mcp:approve`.
1. **Claude organization.** An owner of the client's Claude Team or Enterprise organization adds a custom connector for everyone, with the URL and the OAuth client id (organization settings, connectors; the exact menu names change, check the current Claude documentation). Each person then connects once and signs in with their Virto account.
1. **Routines.** The scope's MCP prompts appear in Claude as ready-made routines (for example a weekly review); longer playbooks can also be shared with the organization as Claude skills.
1. **Operations.** The client's administrators use the back office: approvals, the audit log, tool switches and connected agents.

A listing in Claude's public connector directory is for a product every Virto Commerce customer could use, and goes through Anthropic's review; a client's own solution stays a custom connector of that client's organization. Once the platform supports client ID metadata documents ([VCST-6639](https://virtocommerce.atlassian.net/browse/VCST-6639)), the OAuth client step goes away: the URL alone is enough.

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

- `IMcpPublicOriginResolver` decides the public origin of a scope, for example from a store URL instead of configuration. For one scope, declare it with the scope (`WithPublicOriginResolver<T>()`, see [Add a scope](#add-a-scope)); replacing the registration changes it for every scope.
- `McpChallengeWriter` writes the HTTP 401 and 403 responses of the MCP endpoints; a module with an error format of its own replaces the bodies and keeps the status codes and the `WWW-Authenticate` header. For one scope, declare it with the scope (`WithChallengeWriter<T>()`).
- `IMcpSessionService` is for tools: it tells whether the user's token is still active and signs the user out. The endpoints check tokens with `McpTokenValidator` (see below).
- `IMcpJobDispatcher` starts and tracks the jobs of bulk tools.
- `IMcpPageSizeResolver` decides how many rows a list tool returns.
- `IMcpApprovalService` implements the pending change lifecycle; `McpApprovalService` has virtual methods for the self-approval and expiration rules, and `CanReviewAsync` for which changes an approver may review.
- `IMcpCleanupService` decides what the cleanup deletes.
- `McpPendingChangeReviewService`, `McpCallToolFilter`, `McpPendingChangeExecutor`, `McpAuditWriter`, `McpToolAuthorizationService`, `McpToolAvailabilityService` and `McpTokenValidator` have virtual methods for the back-office review, the execution pipeline, the execution of an approved change, the audit entry, the tool permission check, the tool switches and the token check.

## Components

### Projects

| Project | Layer | Purpose |
| --- | --- | --- |
| `VirtoCommerce.MCP.Core` | Core | Models, service interfaces, attributes (`[McpScope]`, `[McpPermission]`, `[McpAnyUser]`, `[McpRequiresApproval]`, `[McpRequiresConfirmation]`, `[McpRequiresUser]`, `[McpSensitive]`, `[McpApprovalTool]`, `[McpApproverOnly]`), registration extensions, diagnostics. The only package a module with tools references. |
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
| `McpSessionService` | `IMcpSessionService` | Sign-out, and token validity for tools |
| `McpConnectedAgentService` | | Agents connected to MCP endpoints (OAuth clients with an MCP resource) and their disconnection |

### REST API

Base route: `api/mcp`

| Method | Endpoint | Permission | Description |
| --- | --- | --- | --- |
| `GET` | `/scopes` | signed in | Scopes, with whether the user approves in each |
| `GET` | `/summary` | signed in | Changes waiting for the user, failed calls of the last day, switched-off tools |
| `POST` | `/pending-changes/search` | approver of the searched scopes | Search submitted changes |
| `GET` | `/pending-changes/{id}` | approver of the change's scope | A change with its arguments, preview and result |
| `POST` | `/pending-changes/{id}/approve` | approver of the change's scope, allowed to run the tool | Approve and run a change; body `{ "comment": "..." }` |
| `POST` | `/pending-changes/{id}/reject` | approver of the change's scope | Reject a change; body `{ "comment": "..." }` |
| `POST` | `/audit/search` | `mcp:audit:read` | Search the audit log, by tool, outcome, scope, user, trace id, pending change or date range |
| `GET` | `/tools` | `mcp:manage` | The tools of every scope and whether they are switched on |
| `PUT` | `/tools/availability` | `mcp:manage` | Switch a tool, or a scope with `*`, on or off; body `{ "scopeName", "toolName", "enabled" }` |
| `POST` | `/connected-agents/search` | signed in; everyone's with `mcp:manage` | Agents users signed in to, with their client, endpoint and activity; `keyword` matches the client id, the client name or the user name |
| `DELETE` | `/connected-agents/{id}` | the agent's user, or `mcp:manage` | Disconnect an agent: revoke its authorization and tokens |

MCP endpoints: `POST /{scope}/mcp`, `POST /mcp` on a host of a scope, and the protected resource metadata at `GET /.well-known/oauth-protected-resource/{scope}/mcp` and, for a scope host, `GET /.well-known/oauth-protected-resource/mcp`. A `tools/list` request that accepts only `application/json`, as registry crawlers and health checks send, gets a plain JSON answer instead of HTTP 406.

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
* [UCP module](https://github.com/VirtoCommerce/vc-module-ucp): shopping assistants for buyers, served as the `ucp` scope
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
