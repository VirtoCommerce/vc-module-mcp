# Virto Commerce MCP Module

## Overview

The module is the platform-level MCP infrastructure. It hosts the MCP transport, resolves the scope of each request, authenticates the caller, checks permissions, executes tool calls through a common pipeline, holds changes for human approval, and audits every call. Feature modules contribute tools, prompts and resources declaratively and focus on business logic only.

Built on the official C# SDK, [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore).

## How it works

### Scopes

A scope is an MCP surface for one kind of agent, served at `/{scope}/mcp`. Only the primitives marked for a scope are visible and callable on it.

| Scope | Endpoint | Registered by | Who connects |
| --- | --- | --- | --- |
| `admin` | `/admin/mcp` | this module | back-office users with the `mcp:access` permission |
| `vendor` | `/vendor/mcp` | this module | vendor portal users with the `vendor_portal:access` permission (defined by the marketplace vendor module; without it the scope refuses everyone) |
| `ucp` | `/ucp/mcp` | the UCP module | buyers and shopping agents |

Every request is stateless: the scope and the user are resolved per HTTP request, so replicas need no session affinity and one deployment can serve all scopes side by side.

A module can register its own scope, or replace the description of a built-in one (the last registration of a name wins):

```csharp
serviceCollection.AddMcpScope(new McpScope
{
    Name = "category-manager",
    Title = "Category Manager",
    Instructions = "Tools for category managers...",
    RequireAuthenticatedUser = true,
    Permissions = ["catalog:access"],
});
```

### Adding tools to a scope

Tools are ordinary SDK tool types. Mark the class or the method with `[McpScope]`, protect it with the usual platform permission through `[Authorize]`, and register the type from the module's `Initialize`:

```csharp
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[Authorize(MarketingConstants.Security.Permissions.Read)]
public class PromotionTools
{
    [McpServerTool(Name = "search_promotions", ReadOnly = true)]
    [Description("Search promotions. Returns compact rows.")]
    public Task<object> SearchPromotions(string query, int take = 20) { ... }

    [McpServerTool(Name = "activate_promotion", Destructive = true)]
    [Description("Activate a promotion.")]
    [Authorize(MarketingConstants.Security.Permissions.Update)]
    [McpRequiresApproval]
    public Task<object> ActivatePromotion(string promotionId, bool dryRun = false) { ... }
}

// Module.Initialize
serviceCollection.AddMcpTools<PromotionTools>();
```

- `AddMcpPrompts<T>()` and `AddMcpResources<T>()` register prompts and resources the same way.
- A static class cannot be a type argument: register it with `AddMcpTools([typeof(MyTools)])`, or register every tool class of an assembly with `AddMcpToolsFromAssembly(assembly)`. Prompts and resources have the same overloads.
- A primitive without `[McpScope]` is not exposed on any registered scope.
- Names must be unique within a scope; the same name can be used in different scopes. A name registered twice in one scope is reported as an error in the log at startup, and only the first registration is served.
- `[Authorize]` hides the tool from `tools/list` and refuses `tools/call` for users without the permission. The check happens before the tool runs.
- Tool methods can take any registered service as a parameter, as well as `RequestContext<CallToolRequestParams>` and `CancellationToken`. `HttpContext.GetMcpScope()` returns the scope of the current request.

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
- Register the whole project with one line in `Module.Initialize`: `serviceCollection.AddMcpToolsFromAssembly(typeof(PromotionTools).Assembly)`, and the same for prompts and resources when the project has them.
- Declare the dependency on `VirtoCommerce.MCP` in `module.manifest`.

### Common execution pipeline

Every `tools/call` on a registered scope goes through the same steps:

1. The authentication rules of the scope and the permission checks of the tool.
2. Validation of the required arguments.
3. For a tool marked `[McpRequiresApproval]`, the call is stored as a pending change instead of being executed. If the tool has a `dryRun` (or `dry_run`) parameter, it is called once in dry-run mode, and the result is stored as the preview shown to the approver.
4. Logging and an audit entry: user, OAuth client, tool, arguments, outcome and duration.

### Approval

Changes are reviewed in the `admin` scope with these tools. A user with the `mcp:approve` permission works with every change; any other user of the scope sees and withdraws only the changes they requested.

| Tool | Purpose | Who |
| --- | --- | --- |
| `list_pending_changes` | Compact list of submitted changes, by status. | approvers: all changes; others: their own |
| `get_pending_change` | Arguments, dry-run preview, reviewer and result of one change. | approvers: any change; others: their own |
| `approve_pending_change` | Approves the change and executes the tool with the stored arguments, as the approver. | approvers only |
| `reject_pending_change` | Rejects the change. The requester uses it to withdraw their own change. | approvers: any change; others: their own |

### Authentication

MCP endpoints accept only bearer tokens issued by the platform for the exact MCP resource (RFC 8707 audience). Cookies, API keys and basic authentication are ignored on them. An anonymous request to a scope that requires a user gets an HTTP 401 challenge pointing to the scope's protected resource metadata (RFC 9728), which starts the platform OAuth flow in the MCP client:

```
GET /.well-known/oauth-protected-resource/{scope}/mcp
```

On every request the token is also looked up in the platform token store, so a revoked token or sign-in session stops working on all replicas right away.

> [!NOTE]
> A token without an id, issued while the platform token storage is disabled, cannot be looked up, so its revocation is not checked: it works until it expires. Keep the token storage enabled on deployments with MCP scopes.

### Coexistence with modules that host their own MCP endpoint

A module that maps its own MCP endpoint, as UCP currently does at `/ucp/mcp`, shares the same MCP server. On such an endpoint, primitives marked with `[McpScope]` are removed, so scoped tools never appear where scopes are not enforced. The module keeps its endpoint until it moves to a registered scope.

## Rules

### Approval

- By default a change cannot be approved by the user who requested it (setting `MCP.Approval.AllowSelfApproval`).
- The approver must also be allowed to run the tool.
- A change expires after `MCP.Approval.PendingChangeLifetimeHours` and can no longer be approved.
- A change is executed at most once, even if two approvers, or two replicas, approve it at the same time.
- A call with `dryRun: true` changes nothing, so it runs right away instead of being submitted.
- Changes are approved in the `admin` scope only. In any other scope, a call to a tool marked `[McpRequiresApproval]` is refused, and the tool is reported as an error in the log at startup. So is such a tool without `[McpScope]`, because on endpoints without a scope its calls are not held for approval.
- An approved change runs to the end and its result is recorded, even if the approver's client disconnects.

### Audit

Every call made through a registered scope is audited with one of the outcomes `Succeeded`, `PendingApproval`, `Forbidden`, `Failed` (the tool returned an error or refused the request with an `McpException`) or `Error` (unknown tool, invalid arguments, unexpected error). A tool that acts on a pending change links its audit entry to it with `requestContext.SetAuditPendingChangeId(changeId)`.

### Cleanup

The recurring job `MCP.Cleanup` deletes audit entries, and applied, failed, rejected and expired changes, older than the retention settings. A pending change that expired before the retention period is deleted too. An approved change that never completed is kept: it needs a person to look at it.

## Configuration

### appsettings.json

```json
"MCP": {
  "PublicOrigin": "https://admin.example.com",
  "Scopes": {
    "vendor": { "Enabled": false },
    "ucp": { "PublicOrigin": "https://store.example.com" }
  }
}
```

| Key | Description |
| --- | --- |
| `MCP:PublicOrigin` | Origin advertised in the OAuth metadata and expected in token audiences. Defaults to the origin of the request. |
| `MCP:Scopes:{scope}:Enabled` | Switches a scope off. Defaults to `true`. |
| `MCP:Scopes:{scope}:PublicOrigin` | Origin of one scope, for example the storefront host for a buyer scope. |

Invalid origins stop the platform at startup: an origin is an absolute `http` or `https` address without a path, query or fragment.

### Connecting a client to the admin scope

1. Allow the platform to issue tokens for the resource:

    ```json
    "Authorization": {
      "Resources": [ "https://admin.example.com/admin/mcp" ]
    }
    ```

1. Register the OAuth client through `POST /api/platform/oauthapps` with the permission `rsrc:https://admin.example.com/admin/mcp`, the redirect URI of the MCP client, and PKCE.
1. Grant the user the `mcp:access` permission, plus the permissions of the tools they will use.

> [!NOTE]
> On a deployment where `Authorization:OAuthLoginPath` points to the storefront login page, the platform host needs the per-host login path from [VirtoCommerce/vc-platform#3121](https://github.com/VirtoCommerce/vc-platform/pull/3121) for the `admin` scope sign-in to work.

### Settings

| Setting | Default | Description |
| --- | --- | --- |
| `MCP.Approval.AllowSelfApproval` | `false` | Allow a user to approve their own change. |
| `MCP.Approval.PendingChangeLifetimeHours` | `72` | Lifetime of a pending change. |
| `MCP.Audit.Enabled` | `true` | Audit tool calls made through MCP scopes. |
| `MCP.Audit.LogArguments` | `true` | Store tool call arguments in the audit log. |
| `MCP.Cleanup.Enabled` | `true` | Run the recurring cleanup job (`MCP.Cleanup`). |
| `MCP.Cleanup.Cron` | `0 3 * * *` | Cleanup schedule, in UTC. |
| `MCP.Cleanup.AuditRetentionDays` | `90` | Audit entries older than this are deleted. |
| `MCP.Cleanup.PendingChangeRetentionDays` | `90` | Applied, failed, rejected and expired changes older than this are deleted. An approved change that never completed is kept for investigation. |

### Permissions

| Permission | Description |
| --- | --- |
| `mcp:access` | Connect AI agents to the `admin` scope. |
| `mcp:approve` | Review, approve and reject submitted changes. |
| `mcp:audit:read` | Read the tool call audit log. |

### Web API

| Method | Route | Permission |
| --- | --- | --- |
| `POST` | `api/mcp/pending-changes/search` | `mcp:approve` |
| `POST` | `api/mcp/audit/search` | `mcp:audit:read` |
| `GET` | `/.well-known/oauth-protected-resource/{scope}/mcp` | anonymous |

## Extending

Tools, prompts and resources of other modules are added with `AddMcpTools`, `AddMcpPrompts` and `AddMcpResources`, and a module can add its own scope with `AddMcpScope` (see [Adding tools to a scope](#adding-tools-to-a-scope)).

The behaviour of the module can be replaced through DI:

- `IMcpPublicOriginResolver` decides the public origin of a scope, for example from a store URL instead of configuration.
- `IMcpApprovalService` implements the pending change lifecycle; `McpApprovalService` has virtual methods for the self-approval and expiration rules.
- `IMcpCleanupService` decides what the cleanup deletes; `McpCleanupService.GetObsoleteAuditEntries` and `GetObsoletePendingChanges` are virtual.
- `McpCallToolFilter`, `McpPendingChangeExecutor`, `McpAuditWriter`, `McpToolAuthorizationService` and `McpTokenValidator` have virtual methods for the execution pipeline, the execution of an approved change, the audit entry, the tool permission check and the token check.

## References

* [Model Context Protocol specification](https://modelcontextprotocol.io/specification)
* [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
* [Deployment](https://docs.virtocommerce.org/platform/developer-guide/Tutorials-and-How-tos/Tutorials/deploy-module-from-source-code/)
* [Installation](https://docs.virtocommerce.org/platform/user-guide/modules-installation/)
* [Home](https://virtocommerce.com)
* [Community](https://www.virtocommerce.org)

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
