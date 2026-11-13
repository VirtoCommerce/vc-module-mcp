# Samples

Modules that put tools on the MCP module's `admin` scope, from a minimal one to a complete toolset for a role and a client project that extends it. Read them to see how a module builds its tools; run them on a test platform to show an AI agent at work in the back office.

They are examples, not production modules: the marketing samples work on mock data in memory, and the sample module changes only users whose name starts with `demo-`.

## What is here

| Folder | What it is | Size | Start here to learn |
| --- | --- | --- | --- |
| [`VirtoCommerce.MCP.SampleModule`](VirtoCommerce.MCP.SampleModule/README.md) | The smallest module with MCP tools: find, lock, unlock and change platform users named `demo-*` | 4 tools | How a module registers tools, protects them with a permission, asks for approval and hides personal data |
| [`VirtoCommerce.MCP.MarketingSample`](VirtoCommerce.MCP.MarketingSample/README.md) | A marketer's complete toolset: promotions, coupons and storefront content, on realistic mock data | 35 tools, 3 prompts, 3 resources | How to design a toolset for a role: orientation, finders, analysis, actions with previews and approval, bulk jobs, prompts and resources |
| [`VirtoCommerce.MCP.MarketingExtensionSample`](VirtoCommerce.MCP.MarketingExtensionSample/README.md) | "Contoso", a client project that adds its own finance and margin rules to the marketing sample without changing it | 2 tools, 1 prompt, 1 resource | How a partner extends another module's toolset: a derived model, an overridden service, new tools in the same scope |
| `VirtoCommerce.MCP.MarketingExtensionSample.Tests` | Unit tests of the extension, and the conventions check of both marketing samples | 17 tests | How to test tools: registration, approval conditions, the extended rules |

## Which one to read first

1. **[Sample module](VirtoCommerce.MCP.SampleModule/README.md)**, to see the moving parts in four tools. Its README is a ten-minute demo: Claude Code signs in, calls the tools, and a second user approves its change.
2. **[Marketing sample](VirtoCommerce.MCP.MarketingSample/README.md)**, to see what a toolset for a person looks like. Its README follows a marketer's week with it, and [Build an MCP toolset for a role](../docs/role-toolsets.md) explains the method behind it.
3. **[Marketing extension sample](VirtoCommerce.MCP.MarketingExtensionSample/README.md)**, when you build a solution on top of a module's tools.

## Where to find each feature

| Feature | Sample | Look at |
| --- | --- | --- |
| Register tools with one line, `AddMcpCapabilities<T>()` or `AddMcpTools<T>()` | All | `Module.cs` |
| A tool in the `admin` scope protected with a platform permission | Sample module | `Tools/` |
| A page size an agent cannot exceed, `IMcpPageSizeResolver` | Sample module, marketing sample | `search_users`, `search_promotions` |
| Approval by a second person, `[McpRequiresApproval]`, with a `dryRun` preview | Sample module, marketing sample | `lock_user`, `extend_promotion`, `schedule_promotion` |
| Approval only for risky arguments, `Condition` | Marketing sample, extension sample | `generate_coupons`; `ApprovalConditionTests` |
| Confirmation in the chat, `[McpRequiresConfirmation]` | Marketing sample | `pause_promotion` |
| A preview of a change, `McpChangePreview` | Marketing sample | `Tools/PromotionActionTools.cs` |
| A bulk tool on a background job, `IMcpJobDispatcher` | Marketing sample | `generate_coupons`, `deactivate_stale_coupons`, `Jobs/` |
| Personal data hidden in the audit log, `[McpSensitive]` | Sample module | `update_user_email` |
| Permissions limited to stores, `[McpPermission]`, and narrowing the data by every store id | Marketing sample | `Services/MarketingAccess.cs`, used by every tool |
| Prompts for the role's routines | Marketing sample, extension sample | `Prompts/`: `weekly_marketing_review`, `plan_campaign`, `promotion_post_mortem`, `finance_readiness_check` |
| Resources for policies and playbooks | Marketing sample, extension sample | `Resources/`: `marketing://policies/discounts`, `marketing://playbooks/sale-event`, `marketing://calendar/retail-events`, `contoso://policies/finance` |
| Extending a toolset: `AbstractTypeFactory<T>.OverrideType`, a service registered in place of the original | Extension sample | `Module.cs`, `Services/` |
| Checking a toolset against the conventions, `McpToolsetConventions` from `VirtoCommerce.MCP.Testing` | Extension sample tests | `ToolsetConventionsTests` |

## Build and install

The samples are projects of `VirtoCommerce.MCP.sln` and build with it. To run one on a platform that has `VirtoCommerce.MCP` installed:

1. Build the sample.
2. Copy its `module.manifest` into `modules/<module id>` of the platform, and the `<module id>.*` files from `bin/Debug/net10.0` into `modules/<module id>/bin`.
3. Install the modules it depends on, listed in its `module.manifest`: the marketing samples need `VirtoCommerce.Marketing` and `VirtoCommerce.Content`, and `VirtoCommerce.BackgroundJobs` for the bulk tools.
4. Restart the platform, allow tokens for `/admin/mcp`, register an OAuth client for the agent and create the roles, as the README of each sample describes.

Each sample's README has its exact setup and what to ask the agent.

## See also

* [MCP module README](../README.md): how to connect an agent, set up the platform, and the developer guide
* [Build an MCP toolset for a role](../docs/role-toolsets.md): the method behind the marketing sample
* [System Operations](https://github.com/VirtoCommerce/vc-module-system-operations#ai-agents-mcp) and [UCP](https://github.com/VirtoCommerce/vc-module-ucp): real modules that serve toolsets for the IT operator and the buyer
