# MCP Sample Module

A minimal module that adds back-office tools through `VirtoCommerce.MCP`, and a ten-minute demo of what the MCP module guarantees to every tool. It references only `VirtoCommerce.MCP.Core` and declares the runtime dependency on `VirtoCommerce.MCP` in `module.manifest`.

The tools change only platform users whose name starts with `demo-`, so the demo cannot lock a real account.

> [!WARNING]
> The sample is for local demos only. Do not install it on a shared or production environment.

## Overview

| Tool | Shows |
| --- | --- |
| `search_users` | A read-only tool with a platform permission and a page size from `IMcpPageSizeResolver` |
| `lock_user`, `unlock_user` | `[McpRequiresApproval]` with a `dryRun` preview |
| `update_user_email` | A `[McpSensitive]` argument |

| File | Demonstrates |
| --- | --- |
| [`Tools/UserTools.cs`](Tools/UserTools.cs) | A tool class with `[McpScope]`, `[Authorize]`, `[McpRequiresApproval]` and `[McpSensitive]` |
| [`Module.cs`](Module.cs) | Registering the tools with `AddMcpTools<T>()` |

The sample keeps its single tool class in the module project to stay small. A real module keeps its MCP code in a project of its own and registers it with `AddMcpCapabilities<McpAssemblyMarker>()`, as described in [Where to put the MCP code of a module](../../README.md#where-to-put-the-mcp-code-of-a-module).

### What the demo shows

The demo connects Claude Code to the `admin` scope of a local platform:

| Step | What it shows |
| --- | --- |
| The agent signs in through the platform sign-in page | OAuth with the platform's own accounts; the agent never sees the password |
| The agent finds users | It works as the signed-in user and sees only the tools that user has permissions for |
| The agent locks a user | A tool marked `[McpRequiresApproval]` does not run: the call becomes a pending change with a dry-run preview |
| Another person approves the change | The change runs once, as the approver; the requester cannot approve their own change |
| An administrator switches the tool off | `MCP.Tools.Disabled` takes effect on the next request, with no release or restart |
| The agent changes an email | A `[McpSensitive]` argument reaches the tool but is stored as `***` in the audit |
| The audit is searched | Every call is recorded with the user, the tool, the arguments and the outcome |

## Setup

The examples use `http://localhost:10645` as the platform URL. Replace it with yours.

You need:

- a local platform with the `VirtoCommerce.MCP` module installed;
- [Claude Code](https://code.claude.com/docs) (`npm install -g @anthropic-ai/claude-code`). Any other MCP client that supports OAuth with a pre-registered client works the same way;
- an administrator account on the platform.

### 1. Install the sample module

Build the sample and make it visible to the platform the same way as any module under development: the platform loads modules found under `VirtoCommerce:DiscoveryPath`, so link the repository folder there.

```bash
dotnet build samples/VirtoCommerce.MCP.SampleModule
```

The module is loaded on the next start of the platform, after step 2.

### 2. Allow tokens for the MCP endpoint

Add the address of the `admin` scope to the platform `appsettings.json` (or `appsettings.Development.json`):

```json
"Authorization": {
  "Resources": [ "http://localhost:10645/admin/mcp" ]
}
```

Restart the platform: it picks up the setting and loads the sample module. The **Modules** page lists *Model Context Protocol Sample*. Check the endpoint: `GET http://localhost:10645/.well-known/oauth-protected-resource/admin/mcp` returns the resource and the authorization server.

### 3. Register the OAuth client

The platform does not register MCP clients on the fly, so the client is registered once. As an administrator, open **Security → OAuth applications** in the Admin Shell or the classic Manager, select **Add** and fill in:

| Field | Value |
| --- | --- |
| Display name, Client ID | `MCP demo`, `mcp-demo` |
| Client type | Public (**Require PKCE** switches on) |
| Consent | Explicit |
| Grant types | Authorization code, Refresh token |
| Redirect URIs | `http://localhost:65111/callback` |
| Resources | *MCP: Virto Commerce Back Office*, `http://localhost:10645/admin/mcp` |

- **Resources** allows the client to request tokens for the MCP endpoint (a `rsrc:` permission).
- The redirect URI is where Claude Code listens after the sign-in; the port must match `--callback-port` in step 5.
- **Consent** *Explicit* shows the platform consent page on the first sign-in, naming the agent; *Implicit* skips it.

[Set up the platform for agents](../../README.md#set-up-the-platform-for-agents) shows the screen.

### 4. Create roles and users

In the admin UI, open **Security → Roles** and add two roles:

| Role | Permissions |
| --- | --- |
| `mcp-demo-operator` | `mcp:audit:read`, `platform:security:read`, `platform:security:update` |
| `mcp-demo-approver` | the same, plus `mcp:approve` |

Then open **Security → Users** and add four users with confirmed emails:

| User | Role | Part in the demo |
| --- | --- | --- |
| `mcp-demo-operator` | `mcp-demo-operator` | Asks the agent to do things |
| `mcp-demo-approver` | `mcp-demo-approver` | Approves the changes |
| `demo-alice` | none | Account the agent works with |
| `demo-bob` | none | Account the agent works with |

### 5. Connect Claude Code

Each person gets their own folder, because Claude Code reads MCP servers from the folder it starts in and keeps one sign-in per server name.

Operator:

```bash
mkdir operator && cd operator
claude mcp add --scope project --transport http --client-id mcp-demo --callback-port 65111 virto-operator http://localhost:10645/admin/mcp
claude
```

In Claude Code, run `/mcp`, select `virto-operator` and choose **Authenticate**. The browser opens the platform sign-in page; sign in as `mcp-demo-operator`. `/mcp` then shows the server as connected.

Approver, in a second terminal:

```bash
mkdir approver && cd approver
claude mcp add --scope project --transport http --client-id mcp-demo --callback-port 65111 virto-approver http://localhost:10645/admin/mcp
claude
```

Sign out of the platform in the browser first, otherwise the sign-in page reuses the operator's session. Then run `/mcp` → `virto-approver` → **Authenticate** and sign in as `mcp-demo-approver`.

`claude mcp add` writes the server to `.mcp.json` in the folder. The token is kept by Claude Code in the user profile, not in the folder.

## Scenario

Keep the admin UI open as an administrator, on **Security → Users → demo-alice**.

| # | Where | Do | Expected |
| --- | --- | --- | --- |
| 1 | Operator | "Find the demo users in Virto" | `search_users` returns `demo-alice` and `demo-bob`, not locked |
| 2 | Operator | "Lock demo-alice" | `lock_user` returns a pending change with its id and the preview `will be locked` |
| 3 | Admin UI | Refresh `demo-alice` | Still active: nothing changed without approval |
| 4 | Operator | "Approve that change yourself" | Not possible: the operator has no `approve_pending_change` tool without `mcp:approve` |
| 5 | Operator | "Lock admin" | Refused by the sample: it changes only `demo-*` users. The refusal is audited as `Failed` |
| 6 | Approver | "Which changes wait for approval?", then "Approve it" | The client asks the approver to confirm; the change is `Applied`, run as the approver. A client that cannot ask refuses the call (`Declined`): approve it in **Agent approvals** instead |
| 7 | Admin UI | Refresh `demo-alice` | Locked |
| 8 | Admin UI | **Settings → MCP → Tools → Disabled tools**: enter `lock_user`, save | |
| 9 | Operator | "Lock demo-bob" | The agent no longer has `lock_user` and says so |
| 10 | Admin UI | Clear **Disabled tools**, save | `lock_user` is back |
| 11 | Operator | "Change the email of demo-bob to bob.new@example.test" | The email changes at once: this tool needs no approval, it only marks the email as sensitive |
| 12 | Swagger | `POST /api/mcp/audit/search` (below) | Every call above; `update_user_email` has `"email":"***"` |

Claude Code asks before it runs each tool for the first time; allow it. The model may also ask "are you sure?" before a risky request such as locking `admin`: that is the model being careful, and the scenario relies on the server, not on it.

To repeat the demo, unlock `demo-alice` in the admin UI.

### Search the audit

**Configuration → AI agents (MCP) → Agent audit log** in the back office lists the calls. The same search is in the REST API: sign in to Swagger (`/docs`, definition **VirtoCommerce.MCP**) as a user with `mcp:audit:read`, for example `mcp-demo-operator`, and call `POST /api/mcp/audit/search`.

Last calls, newest first:

```json
{ "sort": "CreatedDate:desc", "take": 20 }
```

One tool:

```json
{ "toolName": "update_user_email", "sort": "CreatedDate:desc", "take": 5 }
```

Refused calls only:

```json
{ "outcomes": [ "Forbidden", "Disabled", "Failed" ], "sort": "CreatedDate:desc" }
```

The criteria also take `scopeName`, `userId`, `pendingChangeId`, `startDate`, `endDate` and `skip`.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `/mcp` shows `ECONNREFUSED` | The platform is not running. Start it, then `/mcp` → **Reconnect** |
| The sign-in ends with an error about the resource | `Authorization:Resources` or the client's **Resources** do not match the URL in `.mcp.json` exactly |
| The browser signs in as the wrong user | The platform session from an earlier sign-in is reused. Sign out of the platform, then **Authenticate** again |
| The sign-in page does not open | Another process uses the callback port. Use another port in both the client's redirect URI and `--callback-port` |
| The agent has no `search_users` | The sample module is not loaded, or the user lacks `platform:security:read` |

## Clean up

Delete the four users, the two roles and the `mcp-demo` OAuth client, remove the sample module from the platform, and remove the resource from `Authorization:Resources` if the platform no longer needs it.
