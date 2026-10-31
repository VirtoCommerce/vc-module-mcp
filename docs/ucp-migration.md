# Moving UCP to the MCP module

UCP hosts its own MCP endpoint at `/ucp/mcp` today, with its own copy of the MCP plumbing. After the move, UCP is responsible for the UCP protocol and its integration with the Experience API only, and the MCP module serves the endpoint as the `ucp` scope.

This page lists what moves, what stays, and the order of the work. File references are to `vc-module-ucp/src`.

## Status

Done in VirtoCommerce/vc-module-ucp#13 (branch `feat/VCST-6155-mcp-sdk-2.3`), on platform 3.1091.0. UCP builds against `VirtoCommerce.MCP.Core` 3.1000.0-alpha.15-vcst-6155-core-mcp-module and depends on the `VirtoCommerce.MCP` module of the same version. The branch:

- deletes the UCP copies of the MCP plumbing (about 1,000 lines of source) and registers the `ucp` scope, a `UcpMcpChallengeWriter` and a `UcpMcpPublicOriginResolver`;
- runs its call filter inside the MCP module's pipeline with `AddMcpCallToolFilter`;
- merges VirtoCommerce/vc-module-ucp#12: storefront carts, `track_order` with `placed_after`, store-managed addresses, and a sign-in from the first request when the store's catalog is closed to anonymous buyers;
- switches UCP off with the MCP module's `MCP:Scopes:ucp:Enabled` instead of a `UCP.Enabled` setting. UCP's gate answers 404 on its REST, discovery and GraphQL paths when the scope is off. The gate and the sign-in rule run from an `IStartupFilter`, ahead of the MCP module's `/ucp/mcp` branch, which a module startup cannot precede;
- resolves the store of each request from its host (the store's URL domain or `VirtoCommerce:Stores:Domains`, then `Origin`/`Referer`, `VirtoCommerce:Stores:DefaultStore` and the only open store) and takes the currency, language, links and public origin from it; `MCP:Scopes:ucp:PublicOrigin` sets a fixed origin;
- moves the business switches to store settings: `UCP.StoreManagedAddresses`, `UCP.CheckoutLinkLifetimeMinutes`, `UCP.CheckoutUrlTemplate`. The single-store `UCP:*` keys stay as fallbacks and log a startup warning.

The 370 UCP tests pass, and CI passes on SQL Server, MySQL and PostgreSQL.

Verified end to end on a local platform with both modules installed, against the previous UCP build (27 scenarios, same results), with the old and with the new configuration:

- Discovery: `/.well-known/ucp` and `/.well-known/oauth-protected-resource/ucp/mcp`.
- `initialize` and `tools/list` on `/ucp/mcp`, served by the MCP module.
- Anonymous catalog, geography, cart, checkout and hosted handoff, continued with the cart's `buyer_id`.
- UCP error envelopes for an unknown store, a missing argument and an unknown order, with the trace id in `_meta.trace_id` and in the text.
- An anonymous `link_buyer_identity` gets the HTTP 401 challenge with UCP's `identity_required` body; a signed-in buyer links and signs out, and the token is then refused.
- UCP calls are audited in the `ucp` scope, with the trace id of the call.
- The UCP tools are not visible in the `admin` scope, and the `admin` tools are not visible in the `ucp` scope.
- A closed catalog challenges `initialize`, `tools/list` and tool calls, but not `logout_buyer`; with the scope off, every UCP path answers 404 and `/admin/mcp` keeps working.
- The store is resolved from the host; a REST search with no store or currency uses the store's; a store setting change applies to the next request.

## What moves to the MCP module

Every row is already available in the MCP module, so the UCP code can be deleted once UCP uses it.

| UCP today | MCP module | Note |
| --- | --- | --- |
| `UcpMcpStartup` and its `startupType` in `module.manifest`: the `/ucp/mcp` branch with routing, authentication and authorization | `UseMcpScopes()`, served at `/{scope}/mcp` | Register the `ucp` scope; delete the startup class. |
| Inline probe middleware in `UcpMcpStartup` (`tools/list` with `Accept: application/json` only) | `McpProbeCompatibilityMiddleware` | The MCP module also joins multi-line event data. |
| `AddMcpServer().WithHttpTransport(Stateless = true)` in `Module.cs` | `AddScopedMcpServer()` | Register the tools with `AddMcpToolsFromAssembly(assembly, UcpMcpSerialization.Options)`. |
| Server name and version | `McpScope.Title` and `McpScope.Version` | Set `Version = ModuleConstants.UcpVersion`. |
| `ModuleConstants.McpInstructions` | `McpScope.Instructions` | Same text. |
| `UcpOAuthMetadataController`, `UcpProtectedResourceMetadata` | `McpOAuthMetadataController` | Same route, fields and scopes. |
| Bearer audience check in `UcpMcpBuyerAuthenticationMiddleware` | `McpScopeAuthenticationMiddleware`, `McpTokenValidator` | |
| `UcpMcpSessionService.IsActive` | `IMcpSessionService.IsActiveAsync` | Same token store query. |
| `UcpMcpSessionService.Logout` | `IMcpSessionService.SignOutAsync` | For `logout_buyer`. |
| 401 challenge with `resource_metadata` | `McpChallengeWriter` | UCP replaces `GetChallengeBody` and `GetForbiddenBody` to keep its `messages[]` body (`identity_required`). |
| Challenge on `tools/call link_buyer_identity` only, anonymous access otherwise | `[McpRequiresUser]` on `link_buyer_identity`, scope with `RequireAuthenticatedUser = false` | Any tool with `[McpRequiresUser]` or `[Authorize]` is challenged for an anonymous caller. |
| Required argument validation in `UcpMcpCallToolFilter` | `McpCallToolFilter` | Returned as a tool error with `isError`, as UCP does. |
| Catch-all for unexpected exceptions | `McpCallToolFilter` | Logged and audited as `Error`. |
| Trace id of a failed call | `_meta.trace_id` of failed calls | |
| `UcpMcpActivityStatusFilter`: the error text of a UCP tool kept out of the SDK span status | `McpActivityStatusFilter`, for every tool | |
| `UcpApplicationInsightsActivityBridge` and `UCP:Observability:EnableApplicationInsightsCompatibilityBridge` | The OpenTelemetry module, `OpenTelemetry:AzureMonitor:ConnectionString` | Removed from UCP, with the optional dependency on the Application Insights module. |
| Scope check and order of the SDK request filters | `AddMcpCallToolFilter<TFilter>(scope)` | |
| Error logging of tool calls | Audit log with user, client, arguments, outcome, duration; `[McpSensitive]` masking | New for UCP: mark buyer personal data (addresses, emails, phone numbers) with `[McpSensitive]`. |
| `UcpPublicOriginResolver` (now `MCP:Scopes:ucp:PublicOrigin`, then the store of the request) | `IMcpPublicOriginResolver.GetOriginAsync` | UCP replaces the resolver for the `ucp` scope with its store lookup, and keeps using it for `/.well-known/ucp`. |
| Store host routing | `MCP:Scopes:ucp:Hosts` | Optional: serve the buyer scope at `https://store.example.com/mcp`. |

Not in either module, by design: rate limits and CORS. They belong to the API gateway or the load balancer in front of the platform.

## What stays in UCP

- The commerce tools (`UcpMcpCommerceTools`, 16 tools), their models and `UcpMcpSerialization`.
- Buyer identity: `link_buyer_identity`, `logout_buyer`, `UcpBuyerContextAccessor`, and the rule that a buyer token has exactly one `sub` different from the client id.
- Error mapping: `UcpMcpErrorResultFactory`, the Experience API envelope passthrough, `UcpException` codes.
- The `store_id` checks and UCP telemetry (`UcpOperationTelemetry`, `UcpDiagnostics`) in a slimmer `UcpMcpCallToolFilter`, an `IMcpCallToolFilter` of the `ucp` scope. Its `UCP {operation}` spans are children of the MCP module's `MCP tool {tool}` span.
- The scope name, `ModuleConstants.McpScopeName` (`ucp`).
- The Experience API integration: `XApiInProcessExecutor`, the `ucp` GraphQL schema, the handoff session stores.
- The UCP REST protocol: the `Ucp*Controller` classes except the OAuth metadata one, `/.well-known/ucp` discovery, the MVC filters and the Swagger filters.

## Steps

1. **Upgrade the SDK.** UCP uses `ModelContextProtocol.AspNetCore` 1.4.0, the MCP module 2.3.0. The platform loads one copy of each assembly for all modules, the newest, so with both modules installed UCP already runs on 2.3.0. On the demo platform (3.1089) UCP's `tools/list` and `tools/call` worked on 2.3.0, but UCP is compiled against 1.4.0, and a 1.x-to-2.x upgrade can break code paths that were not exercised. Reference `VirtoCommerce.MCP.Core` instead of the SDK package, build UCP against 2.3.0, and run its tests.
2. **Declare the dependency** on `VirtoCommerce.MCP` in `module.manifest`.
3. **Register the scope** in `Module.Initialize`:

    ```csharp
    serviceCollection.AddMcpScope(new McpScope
    {
        Name = ModuleConstants.McpScopeName,
        Title = "Virto Commerce UCP",
        Version = ModuleConstants.UcpVersion,
        Instructions = ModuleConstants.McpInstructions,
        RequireAuthenticatedUser = false,
    });
    serviceCollection.AddMcpToolsFromAssembly(typeof(UcpMcpCommerceTools).Assembly, UcpMcpSerialization.Options);
    serviceCollection.AddTransient<McpChallengeWriter, UcpMcpChallengeWriter>();
    serviceCollection.AddMcpCallToolFilter<UcpMcpCallToolFilter>(ModuleConstants.McpScopeName);
    ```

4. **Mark the tools** with `[McpScope(ModuleConstants.McpScopeName)]`, `link_buyer_identity` with `[McpRequiresUser]`, and buyer personal data with `[McpSensitive]`.
5. **Delete** `UcpMcpStartup`, `UcpOAuthMetadataController`, `UcpProtectedResourceMetadata`, the generic parts of `UcpMcpBuyerAuthenticationMiddleware`, `UcpMcpSessionService.IsActive`, and the generic half of `UcpMcpCallToolFilter`. Move what is left of the call filter (store checks, telemetry, error mapping) into an `IMcpCallToolFilter`, registered with `AddMcpCallToolFilter` for the `ucp` scope. Delete the Application Insights bridge and the span status filter.
6. **Replace the origin resolver** with one that keeps the UCP store lookup:

    ```csharp
    public class UcpMcpPublicOriginResolver(IOptions<McpOptions> options, UcpPublicOriginResolver ucpResolver)
        : McpPublicOriginResolver(options)
    {
        public override async Task<string> GetOriginAsync(HttpContext context, McpScope scope)
        {
            return scope?.Name == ModuleConstants.McpScopeName ? await ucpResolver.ResolveAsync(context) : await base.GetOriginAsync(context, scope);
        }
    }
    ```

7. **Run the UCP contract tests.** The release notes describe some behavior as a contract; check these points:
    - The challenge body: kept by `UcpMcpChallengeWriter`.
    - A missing argument: a tool error with `isError`, as before.
    - Malformed JSON-RPC: UCP answered with HTTP 400 and a JSON-RPC error before the SDK saw the request; now the SDK answers. Keep the pre-validation only if a client depends on the HTTP status.
    - `logout_buyer` with an expired or revoked token: UCP let the call through anonymously; the MCP module answers with a 401 challenge before any tool runs. Either accept that (the client signs in again, then signs out) or ask for a hook in the MCP module.

## Found while testing

- With both modules installed, scoped tools are not visible on `/ucp/mcp`, and UCP's tools are not visible on `/admin/mcp`.
- The platform role manager drops permission scopes when a role is created (`CustomRoleManager.CreateAsync` saves the permission names only; `UpdateAsync` keeps the scopes). A store-limited marketer role created through the API gets global permissions until it is saved again. This is a platform issue; it affects every module that uses permission scopes, not MCP only.
