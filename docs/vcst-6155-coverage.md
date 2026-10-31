# VCST-6155: what is covered

How the module and the samples answer the story [VCST-6155](https://virtocommerce.atlassian.net/browse/VCST-6155), its description and its comments.

## Core MCP module

| Requirement | Where |
| --- | --- |
| A dedicated module that hosts all MCP infrastructure and removes duplication across modules | This module. UCP moved to it on the branch `feat/VCST-6155-ucp-mcp-module` of `vc-module-ucp`, verified on a local platform ([Moving UCP to the MCP module](ucp-migration.md)) |
| Declarative registration of tools, resources, prompts | `AddMcpTools*`, `AddMcpPrompts*`, `AddMcpResources*` with `[McpScope]`; [Adding tools to a scope](../README.md#add-tools-to-a-scope) |
| Scopes: storefront, admin, vendor, custom | `AddMcpScope`; `admin` and `vendor` built in, `ucp` reserved; [Scopes](../README.md#scopes) |
| Scope from the request domain (primary proposal) or the URL path (alternative) | Both: `/{scope}/mcp` on any host, and `/mcp` on the hosts of `MCP:Scopes:{scope}:Hosts` |
| Anonymous, authenticated and permission-protected tools, checked before execution | Anonymous scopes; `[McpRequiresUser]`; `[Authorize]` and `[McpPermission]`, checked before the tool runs |
| Marketer bounded by store permission scopes, enforced inside each tool | `[McpPermission]` and `GetMcpPermissionGrant`; refusals with `McpForbiddenException`, audited as `Forbidden` |
| Approver sees pending changes with a full preview and is not the requester | `[McpRequiresApproval]`, dry-run preview, `MCP.Approval.AllowSelfApproval` off by default |
| Approval per scope, with an approver permission and a filter by organization (open question of 2026-10-27) | `McpScope.ApprovalPermission`, off by default outside `admin`; `McpApprovalService.CanReviewAsync` for the organization filter |
| The author works with their own changes without `mcp:approve` (comment of 2026-10-23) | `list_pending_changes`, `get_pending_change`, `reject_pending_change` for the author's own changes |
| Seller sees only their own rows, bounded by the principal | `vendor` scope; `GetMcpPermissionGrant` and `McpForbiddenException` give the tool what it needs to filter by seller |
| Platform operator owns configuration, enablement, rollback levers | `MCP:Scopes:{scope}:Enabled`, `MCP.Tools.Disabled` kill switch, settings |
| Authentication, authorization, logging, scope resolution, registration, execution infrastructure | The common pipeline; [Common execution pipeline](../README.md#tool-call-flow) |
| Rate limits | Not in the module, by decision: they belong to the network infrastructure ([Rate limiting and CORS](../README.md#rate-limiting-and-cors)) |
| Sensitive data, `take` limit, kill switch (comment of 2026-10-27) | `[McpSensitive]`, `MCP.Tools.MaxTake`, `MCP.Tools.Disabled` |
| Long-running operations: a tool starts a background job, other tools return its status or cancel it (comment of 2026-10-27) | `IMcpJobDispatcher`, `get_job_status`, `cancel_job` |
| Approval only for risky calls: coupons over a threshold, content that goes live today (comment of 2026-10-05) | `[McpRequiresApproval(Condition = ...)]` |
| Each intent-level action takes `dryRun` and returns a diff | `McpChangePreview` |
| Bulk tools return a job handle and a preview of the first items | `McpJobHandle` |

## Marketing sample (toolset 1 of the comment of 2026-10-05)

| Proposed tool | Sample tool |
| --- | --- |
| `promotions.get_calendar` | `get_promotion_calendar`, with the retail events each promotion covers |
| `promotions.describe_rule_vocabulary` | `describe_promotion_rules` |
| `promotions.search` | `search_promotions` |
| `promotions.get` with a natural-language explanation | `get_promotion` (`explanation`) |
| `promotions.simulate` | `simulate_promotion` |
| `promotions.check_conflicts` | `find_promotion_conflicts` (all, or one promotion) |
| `promotions.explain_evaluation` | `explain_promotion_evaluation` |
| `promotions.create_draft` | `create_promotion_draft` |
| `promotions.update_draft`, only on disabled promotions | `update_promotion_draft`, only on drafts and paused promotions |
| `promotions.activate` with conflicts and simulation in the approval card, `promotions.deactivate` | `activate_promotion`, `schedule_promotion` (approval, preview with conflicts and simulation), `pause_promotion` |
| `promotions.clone` | `clone_promotion` |
| `promotions.schedule_end` | `extend_promotion` |
| `coupons.generate` with a job handle and the first codes; approval over a threshold | `generate_coupons`, approval above 1,000 codes with `[McpRequiresApproval(Condition = ...)]` |
| `coupons.get_usage` | `get_coupon_usage` |
| Guard rails (toolset 3): segments, promotion revenue, margin floor | `list_customer_segments`, `analyze_promotion_performance`; `get_margin_floors` in the extension sample |

Also in the sample, beyond the proposal: `get_marketing_overview`, `find_promotion_issues`, `set_promotion_translation`, `deactivate_stale_coupons`, three prompts and three resources.

Scenarios of the comment and where the [marketer guide](marketing-demo.md) shows them: launch a seasonal campaign (Tuesday), diagnose a coupon complaint (Wednesday), weekly promo hygiene (Monday), stress-test a discount and replicate a campaign across stores (Thursday).

## Marketing sample (toolset 2: `marketing_content`)

| Proposed tool | Sample tool |
| --- | --- |
| `content.get_placement_map` | `get_placement_map` |
| `content.search_items` | `search_content_items` |
| `content.list_placeholders` | `list_content_placeholders` |
| `content.find_gaps` | `find_content_gaps` (empty slots, no successor, unreachable audiences, banners of promotions that are not live) |
| `content.explain_resolution` | `explain_content_resolution` |
| `content.create_item` | `create_content_item` |
| `content.publish`, approval when the window includes today | `publish_content` with `[McpRequiresApproval(Condition = nameof(GoesLiveToday))]` |
| `content.unpublish` / `content.reschedule` | `unpublish_content`, `reschedule_publication` |
| `content.clone_campaign` with a job handle | `clone_content_campaign` |

| `content.propose_copy` → `content.apply_copy(changeId)` | `propose_content_copy`, a pending change with the old and the new text; `approve_pending_change` applies it |

The "personalise without touching code" scenario of the comment is the content part of Thursday in the [marketer guide](marketing-demo.md): `list_content_placeholders`, `publish_content` with a segment, and `explain_content_resolution` for a customer of the segment and one outside it.

## Open questions of the comment of 2026-10-27

Answered by the implementation: the job pattern for long-running operations; authentication per scope (required, or anonymous with sign-in on demand per tool); the UCP split (phase 4), done on the UCP branch. Approval in other scopes is available as a capability and stays off outside `admin` until a module turns it on. The back-office UI is the module's Admin Shell plugin: the approval queue with the diff of each change, the audit log and the tool switches per scope and per tool (README, *Back office*). Tool calls, reviews and jobs emit OpenTelemetry traces, metrics and logs under `VirtoCommerce.MCP`, and each audit entry keeps its trace id (README, *Observability*). Still open, outside this repository: CIMD client registration (a platform task), publishing the `VirtoCommerce.MCP.Core` package, MCP Tasks (not in the C# SDK 2.3.0 yet), and whether UCP's input capture is needed in MCP.
