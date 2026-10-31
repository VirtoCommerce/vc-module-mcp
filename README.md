# Virto Commerce MCP Module

## Overview

The module is the platform-level MCP infrastructure. It hosts the MCP transport, resolves the scope of each request, authenticates the caller, checks permissions, executes tool calls through a common pipeline, holds changes for human approval, and audits every call. Feature modules contribute tools, prompts and resources declaratively and focus on business logic only.

Built on the official C# SDK, [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore).

To see the module at work:

- [Demo: an AI agent in the back office](docs/demo.md): Claude Code signs in to a local platform, calls sample tools, and a second user approves its change.
- [Marketing with an AI agent](docs/marketing-demo.md): a marketer's daily routines (morning check, Black Friday planning, a coupon complaint, stopping a flash sale, approvals) with the [marketing sample](samples/VirtoCommerce.MCP.MarketingSample/README.md), a complete toolset in the shape described in [Designing a toolset](#designing-a-toolset).
- [The extension sample](samples/VirtoCommerce.MCP.MarketingExtensionSample/README.md): a client project that adds its own rules and tools to the marketing toolset without changing it, with unit tests.

## Features

| Feature | What it gives a module | Section |
| --- | --- | --- |
| Scopes | One MCP surface per kind of agent (`admin`, `vendor`, a buyer scope, your own), at `/{scope}/mcp` or at `/mcp` of a host | [Scopes](#scopes) |
| Declarative registration | Tools, prompts and resources are ordinary SDK classes with `[McpScope]`, registered with one line | [Adding tools to a scope](#adding-tools-to-a-scope) |
| Authentication | Platform OAuth with PKCE and an RFC 8707 resource per scope; anonymous scopes; sign-in on demand per tool (`[McpRequiresUser]`); revocation on every request | [Authentication](#authentication) |
| Permissions | `[Authorize]` for global permissions; `[McpPermission]` for permissions limited to stores or sellers, with the scopes available to the tool | [Permissions limited to stores or sellers](#permissions-limited-to-stores-or-sellers) |
| Approval | `[McpRequiresApproval]` holds a call as a pending change with a dry-run preview until a second person approves it, always or only when a condition on the arguments says so | [Approval](#approval) |
| Audit | Every call with user, client, arguments, outcome and duration; `[McpSensitive]` masks personal data | [Audit](#audit) |
| Previews | `McpChangePreview`: one diff shape for dry runs, approvals and results | [Previews](#previews) |
| Bulk jobs | `IMcpJobDispatcher` runs the work of a bulk tool as a platform background job; `get_job_status` and `cancel_job` follow and stop it | [Bulk tools and jobs](#bulk-tools-and-jobs) |
| Kill switch | `MCP.Tools.Disabled` switches a tool, or a scope's tools, off without a release | [Switching tools off](#switching-tools-off) |
| Guard rails for agents | Page size limit for list tools, tool errors the model can correct, trace ids | [Common execution pipeline](#common-execution-pipeline) |

Not in the module, by design: rate limits and CORS, which belong to the infrastructure in front of the platform (see [Rate limiting and CORS](#rate-limiting-and-cors)).

## Setup

1. **Install the module** `VirtoCommerce.MCP` (and the BackgroundJobs module, for bulk tools). It creates its tables on the first start.
1. **Allow tokens for each MCP endpoint** in `appsettings.json`, with the public address of the platform:

    ```json
    "Authorization": {
      "Resources": [ "https://admin.example.com/admin/mcp" ]
    }
    ```

1. **Register the MCP client** once, as an administrator, with `POST /api/platform/oauthapps`: a public client with PKCE, the client's redirect URI and the permission `rsrc:https://admin.example.com/admin/mcp`.
1. **Grant permissions**: `mcp:access` to everyone who connects an agent to the `admin` scope, `mcp:approve` to approvers, plus the permissions of the tools they use.
1. **Install the modules with tools**, such as the [marketing sample](samples/VirtoCommerce.MCP.MarketingSample/README.md) for a demo.
1. **Connect the agent**, for example Claude Code: `claude mcp add --transport http --client-id <client id> --callback-port <port> virto https://admin.example.com/admin/mcp`, then `/mcp` → **Authenticate**.

[Configuration](#configuration) lists every option; [Demo: an AI agent in the back office](docs/demo.md) is a step-by-step walkthrough on a local platform.

## How it works

### Scopes

A scope is an MCP surface for one kind of agent, served at `/{scope}/mcp`. Only the primitives (tools, prompts and resources) marked for a scope are visible and callable on it.

| Scope | Endpoint | Registered by | Who connects |
| --- | --- | --- | --- |
| `admin` | `/admin/mcp` | this module | back-office users with the `mcp:access` permission |
| `vendor` | `/vendor/mcp` | this module | vendor portal users with the `vendor_portal:access` permission (defined by the marketplace vendor module; without it the scope refuses everyone) |
| `ucp` | `/ucp/mcp` | the UCP module, from its VCST-6155 version ([Moving UCP to the MCP module](docs/ucp-migration.md)); earlier UCP versions map the endpoint themselves (see [Coexistence](#coexistence-with-modules-that-host-their-own-mcp-endpoint)) | buyers and shopping agents |

Every request is stateless: the scope and the user are resolved per HTTP request, so replicas need no session affinity and one deployment can serve all scopes side by side.

A scope can also be served at `/mcp` on hosts of its own, for example the storefront host for a buyer scope, or a vendor portal domain. The host is set in configuration, `MCP:Scopes:{scope}:Hosts` (see [appsettings.json](#appsettingsjson)); the scope stays available at `/{scope}/mcp` as well. At `/mcp` of a host, the OAuth resource is that host's address, even when `PublicOrigin` names another host, so a token for `https://store.example.com/mcp` is not accepted at `https://store.example.com/ucp/mcp`, and the other way round.

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

`Version` sets the server version reported to MCP clients; it defaults to the version of this module.

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

- `AddMcpPrompts<T>()` and `AddMcpResources<T>()` register prompts and resources the same way.
- A static class cannot be a type argument: register it with `AddMcpTools([typeof(MyTools)])`, or register every tool class of an assembly with `AddMcpToolsFromAssembly(assembly)`. Prompts and resources have the same overloads.
- A primitive without `[McpScope]` is not exposed on any registered scope.
- Names must be unique within a scope; the same name can be used in different scopes. A name registered twice in one scope is reported as an error in the log at startup, and only the first registration is served.
- `[Authorize]` hides the tool from `tools/list` and refuses `tools/call` for users without the permission. The check happens before the tool runs.
- Tool methods can take any registered service as a parameter, as well as `RequestContext<CallToolRequestParams>` and `CancellationToken`. `HttpContext.GetMcpScope()` returns the scope of the current request.
- In a scope open to anonymous users (`RequireAuthenticatedUser = false`), mark a tool that needs a signed-in user with `[McpRequiresUser]`. The tool is listed to everyone; an anonymous call to it is answered with an HTTP 401 OAuth challenge, so the MCP client signs the user in and repeats the call. An anonymous call to a tool with `[Authorize]` is challenged the same way, but such a tool is not listed to anonymous users.
- A tool that returns a list takes an optional `take` and passes it through `IMcpPageSizeResolver.GetPageSizeAsync(take)`: an omitted value gets 20 rows, as in `SearchCriteriaBase`, and a larger one is capped at the `MCP.Tools.MaxTake` setting, so an agent cannot load thousands of rows into its context with one call. A tool can apply a lower limit of its own.

### Permissions limited to stores or sellers

`[Authorize("marketing:read")]` accepts a permission granted globally only. A user whose role grants `marketing:read` for some stores (a permission scope, such as `MarketingStoreSelectedScope`) does not pass it, and would not see the tool at all. Use `[McpPermission]` instead: the tool is visible to users who have the permission globally or with scopes, and the tool limits the data itself.

```csharp
[McpServerTool(Name = "search_promotions", ReadOnly = true)]
[McpPermission("marketing:read")]
public object SearchPromotions(RequestContext<CallToolRequestParams> context, string store_id = null)
{
    var grant = context.User.GetMcpPermissionGrant("marketing:read");
    var storeIds = grant.GetScopeValues("MarketingStoreSelectedScope"); // null: every store

    if (store_id != null && !grant.Allows("MarketingStoreSelectedScope", store_id))
    {
        throw new McpForbiddenException($"You do not manage the marketing of store '{store_id}'.");
    }
    ...
}
```

- `GetMcpPermissionGrant` reads the scopes as type and value, so the tool does not depend on the scope classes of the module that defines them. A scoped claim that cannot be read grants no scope, never the global permission.
- `McpForbiddenException` returns the message to the agent as the tool's error, and the call is audited as `Forbidden`. Use it for every refusal of a resource the user may not see, such as a row of another seller in the vendor scope.
- `[McpPermission]` and `[Authorize]` can be combined; the user needs both.

> [!NOTE]
> The platform role manager keeps permission scopes only when a role is updated: a role created with scoped permissions in one call is saved with global permissions. Save the role once more after creating it, until this is fixed in the platform.

### Designing a toolset

Give each role a toolset with five layers, in roughly this ratio. The [marketing sample](samples/VirtoCommerce.MCP.MarketingSample/README.md) implements all of them.

| Layer | Count | What it is | The module helps with |
| --- | --- | --- | --- |
| Orientation | 2-3 | "Where am I, what is the state": aggregated, opinionated reads that the model can reason over, such as `get_marketing_overview`. | |
| Finders | 2-4 | Scoped searches returning ids and the 5-8 fields that matter for the decision, never full objects. | `IMcpPageSizeResolver` for the page size |
| Analysis | 1-3 | Answers the platform computes cheaply and the model cannot: promotions over budget, discounts that stack, products without images in a locale. | |
| Intent-level actions | 3-6 | The verbs of the role, not entity saves: `pause_promotion`, `hold_order_for_review`. Each takes `dryRun` and returns a diff. | `McpChangePreview` for the diff, `[McpRequiresApproval]` for a second person |
| Bulk | 1-2 | Accept a list or a filter, start a background job, return a job handle and a preview of the first items. | `IMcpJobDispatcher` and `get_job_status` |

#### Previews

`McpChangePreview` is a common shape for what an action does or did: the action, the object, a list of field changes (`from`, `to`) and warnings for the approver. Return it from the dry run and from the real run, so the agent, the approver and the audit see the same thing:

```csharp
var preview = new McpChangePreview { Action = "extend", ObjectType = "promotion", ObjectId = promotion.Id, DryRun = dryRun }
    .AddChange("end", promotion.EndDate, newEndDate)
    .AddWarning("Estimated extra discount cost: about 30,000 USD.");
```

`get_pending_change` shows the preview and the result as the tool returned them: its structured content, or the JSON of its text.

#### Bulk tools and jobs

A bulk tool hands the work to the platform background job engine through `IMcpJobDispatcher` and returns at once:

```csharp
return await jobDispatcher.EnqueueAsync<GenerateCouponsJob>(requestContext, payload, new McpJobRequest
{
    Title = $"Generate {count} coupons",
    TotalCount = count,
    Preview = firstCodes,
});
```

- The handler is an ordinary `IBackgroundJobHandler<TPayload>`, registered with `AddBackgroundJob`. Its progress reports (`context.Progress.Report`) become the progress of the job, shown in the admin UI and returned by `get_job_status`.
- The result is an `McpJobHandle`: `job_id`, `status`, `title`, `total_count` and the preview. The agent follows the job with `get_job_status` and stops it with `cancel_job` (both in the `admin` and `vendor` scopes). A queued job is canceled at once; a running one stops when the job engine supports cancellation, and is then `Canceled`.
- A job is visible to the user who ran the tool and, when an approved change started it, to the user who requested the change; in the `admin` scope approvers see every job.
- A bulk tool can also require approval: the dry run returns the preview, and the job starts when the change is approved.
- Finished jobs are deleted by the cleanup with the audit entries (`MCP.Cleanup.AuditRetentionDays`).

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

Their values are replaced with `***` in the audit log, in `get_pending_change`, and in the pending changes returned by the Web API. A change waiting for approval keeps the values, because the tool needs them when the change is approved; they are replaced with `***` as soon as the change is applied, fails, is rejected or expires. The dry-run preview and the result of an approved change are stored as the tool returns them, so a tool should not return sensitive values in either.

### Common execution pipeline

Every `tools/call` on a registered scope goes through the same steps:

1. The authentication rules of the scope and the permission checks of the tool (`[Authorize]`, `[McpPermission]`).
2. A tool switched off with `MCP.Tools.Disabled` is refused (see [Switching tools off](#switching-tools-off)).
3. Validation of the required arguments. A missing argument is returned as the tool's error, not as a protocol error, so the model can correct the call.
4. For a tool marked `[McpRequiresApproval]`, the call is stored as a pending change instead of being executed. If the tool has a `dryRun` (or `dry_run`) parameter, it is called once in dry-run mode, and the result is stored as the preview shown to the approver. With a `Condition`, only the calls the condition selects are held; the others run at once (see [Approval](#approval)).
5. Logging and an audit entry: user, OAuth client, tool, arguments, outcome and duration.

A tool refuses a request with `McpException` (audited as `Failed`) or `McpForbiddenException` (audited as `Forbidden`): the agent gets the message as it is, as the tool's error. Every failed call returns the trace id in `_meta.trace_id`, so support can find the call in the logs and the telemetry.

### Approval

When only some calls of a tool are risky, name a condition: a public static method of the tool class that decides from the arguments.

```csharp
[McpRequiresApproval(Condition = nameof(NeedsApproval))]
public Task<object> GenerateCoupons(string promotion_id, int count, bool dryRun = false) { ... }

// One code for a complaint runs at once; a mass mailing waits for a second person.
public static bool NeedsApproval(IReadOnlyDictionary<string, JsonElement> arguments) =>
    !arguments.TryGetValue("count", out var count) || count.GetInt32() > 1000;
```

A condition that cannot be found counts as "needs approval" and is reported as an error in the log at startup, so a typo never skips the approval.

### Approval in other scopes

A scope has approval when its `ApprovalPermission` is set: `mcp:approve` for `admin`. A module turns approval on for another scope, its own or a built-in one, by registering the scope with an approver permission (the last registration of a name wins):

```csharp
serviceCollection.AddMcpScope(new McpScope
{
    Name = McpScopes.Vendor,
    Title = "Virto Commerce Vendor Portal",
    RequireAuthenticatedUser = true,
    Permissions = ["vendor_portal:access"],
    ApprovalPermission = "vendor_portal:approve",
});
```

- The review tools below appear in every scope with approval and work with the changes of that scope only. Their approvers are the users with the scope's `ApprovalPermission`; an approver of `admin` is not an approver of `vendor`.
- To narrow what an approver reviews, for example to the changes of their own seller, override `McpApprovalService.CanReviewAsync`.
- In a scope without approval, a tool marked `[McpRequiresApproval]` is refused, and the error log at startup names it.

Changes are reviewed with these tools. A user with the `mcp:approve` permission works with every change; any other user of the scope sees and withdraws only the changes they requested.

| Tool | Purpose | Who |
| --- | --- | --- |
| `list_pending_changes` | Compact list of submitted changes, by status. | approvers: all changes; others: their own |
| `get_pending_change` | Arguments, dry-run preview, reviewer and result of one change. | approvers: any change; others: their own |
| `approve_pending_change` | Approves the change and executes the tool with the stored arguments, as the approver. | approvers only |
| `reject_pending_change` | Rejects the change. The requester uses it to withdraw their own change. | approvers: any change; others: their own |
| `get_job_status` | Status and progress of a background job started by a bulk tool. Also in the `vendor` scope. | the user who started it, the requester of the change that started it, approvers |
| `cancel_job` | Cancels a queued or running background job. Also in the `vendor` scope. | as `get_job_status` |

### Authentication

MCP endpoints accept only bearer tokens issued by the platform for the exact MCP resource (RFC 8707 audience). Cookies, API keys and basic authentication are ignored on them. An anonymous request to a scope that requires a user gets an HTTP 401 challenge pointing to the scope's protected resource metadata (RFC 9728), which starts the platform OAuth flow in the MCP client:

```
GET /.well-known/oauth-protected-resource/{scope}/mcp
```

On every request the token is also looked up in the platform token store, so a revoked token or sign-in session stops working on all replicas right away.

A tool that signs the user out, such as a buyer's `logout`, calls `IMcpSessionService.SignOutAsync(user)`: the access and refresh tokens of the OAuth sign-in stop working on all replicas, and the MCP client has to sign the user in again.

> [!NOTE]
> A token without an id, issued while the platform token storage is disabled, cannot be looked up, so its revocation is not checked: it works until it expires. Keep the token storage enabled on deployments with MCP scopes.

### Coexistence with modules that host their own MCP endpoint

A module that maps its own MCP endpoint, as UCP currently does at `/ucp/mcp`, shares the same MCP server. On such an endpoint, primitives marked with `[McpScope]` are removed, so scoped tools never appear where scopes are not enforced. The module keeps its endpoint until it moves to a registered scope; [Moving UCP to the MCP module](docs/ucp-migration.md) describes what moves and what stays in UCP.

Both modules must use the same version of the MCP SDK: the platform loads one copy of an assembly, the newest, for all modules.

## Rules

### Approval

- By default a change cannot be approved by the user who requested it (setting `MCP.Approval.AllowSelfApproval`).
- The approver must also be allowed to run the tool.
- A change expires after `MCP.Approval.PendingChangeLifetimeHours` and can no longer be approved.
- A change is executed at most once, even if two approvers, or two replicas, approve it at the same time.
- A call with `dryRun: true` changes nothing, so it runs right away instead of being submitted.
- Changes are approved in scopes with approval only (`McpScope.ApprovalPermission`; `admin` by default). In any other scope, a call to a tool marked `[McpRequiresApproval]` is refused, and the tool is reported as an error in the log at startup. So is such a tool without `[McpScope]`, because on endpoints without a scope its calls are not held for approval.
- An approved change runs to the end and its result is recorded, even if the approver's client disconnects.

### Audit

Every call made through a registered scope is audited with one of the outcomes `Succeeded`, `PendingApproval`, `Forbidden`, `Disabled` (the tool is switched off by an administrator), `Failed` (the tool returned an error or refused the request with an `McpException`) or `Error` (unknown tool, invalid arguments, unexpected error). A tool that acts on a pending change links its audit entry to it with `requestContext.SetAuditPendingChangeId(changeId)`.

### Cleanup

The recurring job `MCP.Cleanup` deletes audit entries, and applied, failed, rejected and expired changes, older than the retention settings. A pending change that expired before the retention period is deleted too. An approved change that never completed is kept: it needs a person to look at it. The job also marks overdue pending changes as expired, which removes their sensitive arguments.

### Switching tools off

An administrator can switch tools off without a release with the `MCP.Tools.Disabled` setting, one entry per line:

| Entry | Switches off |
| --- | --- |
| `activate_promotion` | the tool in every scope |
| `admin/activate_promotion` | the tool in the `admin` scope only |
| `vendor/*` | every tool of the `vendor` scope |

A tool that is switched off disappears from `tools/list`, a call to it is refused with a message the agent can pass on to the user and audited as `Disabled`, and its pending changes cannot be approved until it is switched on again. The setting applies to the next request, with no restart.

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

### Rate limiting and CORS

The module does not limit the request rate and does not add CORS headers: both belong to the infrastructure in front of the platform. Limit the rate of requests to `/*/mcp` in the API gateway or the load balancer, because an agent that goes into a loop can call a tool hundreds of times a minute. CORS is needed only for MCP clients that run in a browser; Claude, ChatGPT and other hosted clients call the server from their own back end.

### Connecting a client to the admin scope

1. Allow the platform to issue tokens for the resource:

    ```json
    "Authorization": {
      "Resources": [ "https://admin.example.com/admin/mcp" ]
    }
    ```

1. Register the OAuth client through `POST /api/platform/oauthapps` with the permission `rsrc:https://admin.example.com/admin/mcp`, the redirect URI of the MCP client, and PKCE.
1. Grant the user the `mcp:access` permission, plus the permissions of the tools they will use.

For a local walkthrough with Claude Code and the sample tools in [`samples/VirtoCommerce.MCP.SampleModule`](samples/VirtoCommerce.MCP.SampleModule/README.md), see [Demo: an AI agent in the back office](docs/demo.md).

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
| `MCP.Tools.Disabled` | empty | Tools switched off, one per line: `tool`, `scope/tool` or `scope/*`. |
| `MCP.Tools.MaxTake` | `50` | Largest number of rows a list tool returns in one call. A tool that is not given a number returns 20 rows, or fewer if this limit is lower. |

### Permissions

| Permission | Description |
| --- | --- |
| `mcp:access` | Connect AI agents to the `admin` scope. |
| `mcp:approve` | Review, approve and reject submitted changes in the `admin` scope. Other scopes name their own approver permission. |
| `mcp:audit:read` | Read the tool call audit log. |

### Web API

| Method | Route | Permission |
| --- | --- | --- |
| `POST` | `api/mcp/pending-changes/search` | `mcp:approve` |
| `POST` | `api/mcp/audit/search` | `mcp:audit:read` |
| `GET` | `/.well-known/oauth-protected-resource/{scope}/mcp` | anonymous |

## How to extend

### Add a toolset to your module

Put the tools, prompts and resources in a `VirtoCommerce.<Module>.MCP` project, mark them with `[McpScope]` and a permission, and register the project with `AddMcpToolsFromAssembly`, `AddMcpPromptsFromAssembly` and `AddMcpResourcesFromAssembly` (see [Adding tools to a scope](#adding-tools-to-a-scope) and [Where to put the MCP code of a module](#where-to-put-the-mcp-code-of-a-module)). A module can add a scope of its own with `AddMcpScope`. [Designing a toolset](#designing-a-toolset) describes the five layers that make a toolset useful, and the [marketing sample](samples/VirtoCommerce.MCP.MarketingSample/README.md) implements them.

### Extend another module's toolset without changing it

A client project changes the behaviour of a module's tools with the platform's usual extension points, in a module of its own that depends on the extended one. The [extension sample](samples/VirtoCommerce.MCP.MarketingExtensionSample/README.md) does all of this to the marketing sample, with unit tests:

| Need | Extension point | In the extension sample |
| --- | --- | --- |
| A new field on a model | `AbstractTypeFactory<T>.OverrideType<T, TDerived>()` | `ContosoPromotion` adds a cost center to every promotion |
| Company rules in existing tools | Register a derived service in place of the original one; the tools resolve it from DI | `ContosoMarketingAnalytics` adds finance and margin rules to `get_promotion`, `find_promotion_issues` and every preview |
| New tools next to the existing ones | `AddMcpToolsFromAssembly` with the same `[McpScope]` | `set_promotion_cost_center`, `get_margin_floors` |
| New prompts and resources | `AddMcpPromptsFromAssembly`, `AddMcpResourcesFromAssembly` | `finance_readiness_check`, `contoso://policies/finance` |
| Settings of the extension | Options bound from configuration | `Contoso:Marketing` |
| A tool that must not be used | `MCP.Tools.Disabled` | |

- Declare the extended module as a dependency in `module.manifest`, so the platform initializes your module after it and your registrations win.
- A tool name is unique in a scope: add tools under new names, and change the behaviour of an existing tool through the services it uses. Tool classes take their services from DI, so a replaced service changes every tool that uses it.
- To be extensible this way, a module creates its models with `AbstractTypeFactory`, registers its services in DI, and makes their methods `virtual`, as the marketing sample does.

### Replace the MCP infrastructure

The behaviour of this module can be replaced through DI:

- `IMcpPublicOriginResolver` decides the public origin of a scope, for example from a store URL instead of configuration. It is asynchronous, so it can look the store up.
- `McpChallengeWriter` writes the HTTP 401 and 403 responses of the MCP endpoints. A module that keeps an error format of its own replaces the bodies (`GetChallengeBody`, `GetForbiddenBody`) and keeps the status codes and the `WWW-Authenticate` header.
- `IMcpSessionService` checks that a token is still valid and signs the user out.
- `IMcpJobDispatcher` starts and tracks the jobs of bulk tools.
- `IMcpPageSizeResolver` decides how many rows a list tool returns.
- `IMcpApprovalService` implements the pending change lifecycle; `McpApprovalService` has virtual methods for the self-approval and expiration rules, and `CanReviewAsync` for which changes an approver may review.
- `IMcpCleanupService` decides what the cleanup deletes; `McpCleanupService.GetObsoleteAuditEntries` and `GetObsoletePendingChanges` are virtual.
- `McpCallToolFilter`, `McpPendingChangeExecutor`, `McpAuditWriter`, `McpToolAuthorizationService`, `McpToolAvailabilityService` and `McpTokenValidator` have virtual methods for the execution pipeline, the execution of an approved change, the audit entry, the tool permission check, the tools that are switched off and the token check.

## References

* [Demo: an AI agent in the back office](docs/demo.md)
* [Marketing with an AI agent](docs/marketing-demo.md)
* [Moving UCP to the MCP module](docs/ucp-migration.md)
* [VCST-6155: what is covered](docs/vcst-6155-coverage.md)
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
