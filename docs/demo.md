# Demo: an AI agent in the back office

This walkthrough connects Claude Code to the `admin` scope of a local platform and shows, in about ten minutes, what the module guarantees to every tool:

| Step | What it shows |
| --- | --- |
| The agent signs in through the platform sign-in page | OAuth with the platform's own accounts; the agent never sees the password |
| The agent finds users | It works as the signed-in user and sees only the tools that user has permissions for |
| The agent locks a user | A tool marked `[McpRequiresApproval]` does not run: the call becomes a pending change with a dry-run preview |
| Another person approves the change | The change runs once, as the approver; the requester cannot approve their own change |
| An administrator switches the tool off | `MCP.Tools.Disabled` takes effect on the next request, with no release or restart |
| The agent changes an email | A `[McpSensitive]` argument reaches the tool but is stored as `***` in the audit |
| The audit is searched | Every call is recorded with the user, the tool, the arguments and the outcome |

The tools come from the sample module [`samples/VirtoCommerce.MCP.SampleModule`](../samples/VirtoCommerce.MCP.SampleModule/README.md). They change only users whose name starts with `demo-`, so the demo cannot lock a real account.

The examples use `http://localhost:10645` as the platform URL. Replace it with yours.

## What you need

- A local platform with the `VirtoCommerce.MCP` module installed.
- [Claude Code](https://code.claude.com/docs) (`npm install -g @anthropic-ai/claude-code`). Any other MCP client that supports OAuth with a pre-registered client works the same way.
- An administrator account on the platform.

## 1. Install the sample module

Build the sample and make it visible to the platform the same way as any module under development: the platform loads modules found under `VirtoCommerce:DiscoveryPath`, so link the repository folder there.

```bash
dotnet build samples/VirtoCommerce.MCP.SampleModule
```

The module is loaded on the next start of the platform, after step 2.

> [!WARNING]
> The sample is for local demos only. Do not install it on a shared or production environment.

## 2. Allow tokens for the MCP endpoint

Add the address of the `admin` scope to the platform `appsettings.json` (or `appsettings.Development.json`):

```json
"Authorization": {
  "Resources": [ "http://localhost:10645/admin/mcp" ]
}
```

Restart the platform: it picks up the setting and loads the sample module. The **Modules** page lists *Model Context Protocol Sample*. Check the endpoint: `GET http://localhost:10645/.well-known/oauth-protected-resource/admin/mcp` returns the resource and the authorization server.

## 3. Register the OAuth client

The platform does not register MCP clients on the fly, so the client is registered once. Sign in to Swagger (`/docs`) as an administrator and call `POST /api/platform/oauthapps`:

```json
{
  "displayName": "MCP demo",
  "clientId": "mcp-demo",
  "clientType": "public",
  "consentType": "implicit",
  "redirectUris": [ "http://localhost:65111/callback" ],
  "postLogoutRedirectUris": [],
  "requirements": [ "ft:pkce" ],
  "permissions": [ "rsrc:http://localhost:10645/admin/mcp" ],
  "properties": {},
  "settings": {}
}
```

- `rsrc:` allows the client to request tokens for the MCP endpoint.
- The redirect URI is where Claude Code listens after the sign-in; the port must match `--callback-port` in step 5.
- Set `"consentType": "explicit"` to show the platform consent screen on the first sign-in.

## 4. Create roles and users

In the admin UI, open **Security → Roles** and add two roles:

| Role | Permissions |
| --- | --- |
| `mcp-demo-operator` | `mcp:access`, `mcp:audit:read`, `platform:security:read`, `platform:security:update` |
| `mcp-demo-approver` | the same, plus `mcp:approve` |

Then open **Security → Users** and add four users with confirmed emails:

| User | Role | Part in the demo |
| --- | --- | --- |
| `mcp-demo-operator` | `mcp-demo-operator` | Asks the agent to do things |
| `mcp-demo-approver` | `mcp-demo-approver` | Approves the changes |
| `demo-alice` | none | Account the agent works with |
| `demo-bob` | none | Account the agent works with |

## 5. Connect Claude Code

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

## 6. Run the scenario

Keep the admin UI open as an administrator, on **Security → Users → demo-alice**.

| # | Where | Do | Expected |
| --- | --- | --- | --- |
| 1 | Operator | "Find the demo users in Virto" | `search_users` returns `demo-alice` and `demo-bob`, not locked |
| 2 | Operator | "Lock demo-alice" | `lock_user` returns a pending change with its id and the preview `will be locked` |
| 3 | Admin UI | Refresh `demo-alice` | Still active: nothing changed without approval |
| 4 | Operator | "Approve that change yourself" | Not possible: the operator has no `approve_pending_change` tool without `mcp:approve` |
| 5 | Operator | "Lock admin" | Refused by the sample: it changes only `demo-*` users. The refusal is audited as `Failed` |
| 6 | Approver | "Which changes wait for approval?", then "Approve it" | The change is `Applied`, run as the approver |
| 7 | Admin UI | Refresh `demo-alice` | Locked |
| 8 | Admin UI | **Settings → MCP → Tools → Disabled tools**: enter `lock_user`, save | |
| 9 | Operator | "Lock demo-bob" | The agent no longer has `lock_user` and says so |
| 10 | Admin UI | Clear **Disabled tools**, save | `lock_user` is back |
| 11 | Operator | "Change the email of demo-bob to bob.new@example.test" | The email changes at once: this tool needs no approval, it only marks the email as sensitive |
| 12 | Swagger | `POST /api/mcp/audit/search` (below) | Every call above; `update_user_email` has `"email":"***"` |

Claude Code asks before it runs each tool for the first time; allow it. The model may also ask "are you sure?" before a risky request such as locking `admin`: that is the model being careful, and the scenario relies on the server, not on it.

To repeat the demo, unlock `demo-alice` in the admin UI.

## 7. Search the audit

Sign in to Swagger (`/docs`, definition **VirtoCommerce.MCP**) as a user with `mcp:audit:read`, for example `mcp-demo-operator`, and call `POST /api/mcp/audit/search`.

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
| The sign-in ends with an error about the resource | `Authorization:Resources` or the client's `rsrc:` permission does not match the URL in `.mcp.json` exactly |
| The browser signs in as the wrong user | The platform session from an earlier sign-in is reused. Sign out of the platform, then **Authenticate** again |
| The sign-in page does not open | Another process uses the callback port. Use another port in both the client's redirect URI and `--callback-port` |
| The agent has no `search_users` | The sample module is not loaded, or the user lacks `platform:security:read` |

## Clean up

Delete the four users, the two roles and the `mcp-demo` OAuth client, remove the sample module from the platform, and remove the resource from `Authorization:Resources` if the platform no longer needs it.
