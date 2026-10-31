# MCP Marketing Sample

A marketer's toolset for an AI agent, built on `VirtoCommerce.MCP` and shaped as described in [Design a toolset](../../README.md#design-a-toolset). It works on realistic mock data in memory, so it can be shown on any platform without touching real promotions. [Marketing with an AI agent](../../docs/marketing-demo.md) shows a week of work with it.

## Features

The tools are in the `admin` scope and protected with the permissions of the Marketing module (`marketing:read`, `marketing:create`, `marketing:update`), which they respect when limited to stores; the content tools use the permissions of the Content module (`content:read`, `content:create`, `content:update`).

| Layer | Tool | What it does | Approval |
| --- | --- | --- | --- |
| Orientation | `get_marketing_overview` | Per store: live, scheduled, draft and paused promotions, spend against the monthly budget, best promotions of the week, what ends or starts soon, top issues, retail events ahead | |
| Orientation | `get_promotion_calendar` | The plan against the retail calendar, with events no promotion covers | |
| Orientation | `describe_promotion_rules` | What a promotion can say: rewards, targets, conditions, how promotions combine | |
| Finder | `search_promotions`, `get_promotion` | Compact rows; one promotion with a plain-language explanation, results, policy warnings and conflicts | |
| Finder | `search_coupons`, `list_customer_segments` | Codes with their use; segments with their size | |
| Analysis | `analyze_promotion_performance` | Revenue per unit of discount, order value uplift, budget used, trend, verdict | |
| Analysis | `find_promotion_issues` | Health check: budget almost spent, above the policy limit, ending while strong, underperforming, no coupons, stale coupons, missing translations | |
| Analysis | `find_promotion_conflicts` | Stacking, shadowing by an exclusive promotion, equal priorities, redundant free shipping; for all promotions or one | |
| Analysis | `simulate_promotion` | A promotion, even a draft, against the last 30 days of orders: hit rate, average discount, exposure, why orders miss it | |
| Analysis | `explain_promotion_evaluation` | Why an order got or did not get each promotion ("my code did not work") | |
| Analysis | `get_coupon_usage` | Unused, used, used-up and deactivated codes, redemptions by day, rejected attempts | |
| Action | `create_promotion_draft`, `update_promotion_draft`, `clone_promotion` | Drafts are invisible to customers | no |
| Action | `set_promotion_translation`, `pause_promotion` | A missing name; stop a promotion at once | no |
| Action | `schedule_promotion`, `activate_promotion` | Go live; the preview includes conflicts and a simulation | yes |
| Action | `extend_promotion`, `change_promotion_discount` | The preview estimates the extra cost or shows the policy check | yes |
| Bulk | `generate_coupons` | Codes created by a background job; the preview shows the first codes | above 1,000 codes |
| Bulk | `deactivate_stale_coupons` | Codes of ended promotions, deactivated by a background job | no |
| Content: orientation | `get_placement_map` | Every placeholder of a store on a date: what is shown, for whom, what comes next, empty slots | |
| Content: finder | `list_content_placeholders`, `search_content_items` | Placeholders by page; banners, HTML blocks and carousels with where they are published | |
| Content: analysis | `find_content_gaps` | Empty slots, publications without a successor, audiences that can never match, banners of promotions that are not live | |
| Content: analysis | `explain_content_resolution` | Which publication a customer of a segment sees on a date, and why the others are not shown | |
| Content: action | `create_content_item`, `unpublish_content` | A new item; take a publication down at once | no |
| Content: action | `publish_content`, `reschedule_publication` | Scheduling for later applies at once; a window that includes today needs approval | when live today |
| Content: action | `propose_content_copy` | New text for a banner, written by the agent from a brief; the old and new text side by side for the approver | yes |
| Content: bulk | `clone_content_campaign` | Copy publications to another store, shifted in time, by a background job | no |

Every action takes `dryRun` and returns an `McpChangePreview`: the changed fields and warnings for the approver.

Prompts: `weekly_marketing_review`, `plan_campaign`, `promotion_post_mortem`. Resources: `marketing://policies/discounts`, `marketing://playbooks/sale-event`, `marketing://calendar/retail-events`.

The demo data: three stores (Electronics, B2B Office Supply, Fashion Outlet), sixteen promotions, coupons, five customer segments, 30 days of orders, and the placeholders, banners and publications of each store, all relative to today and the real retail calendar. Each promotion is there to show one finding; the comments in [`Data/MarketingDemoSeed.cs`](Data/MarketingDemoSeed.cs) say which. Order `CO-10482` is a coupon complaint to explain. The data lives in memory: a restart starts over.

## Setup

1. Install `VirtoCommerce.MCP`, `VirtoCommerce.Marketing`, `VirtoCommerce.Content` and, for the bulk tools, `VirtoCommerce.BackgroundJobs`.
2. Build this project and copy `module.manifest` and the `VirtoCommerce.MCP.MarketingSample.*` files from `bin/Debug/net10.0` into `modules/VirtoCommerce.MCP.MarketingSample/bin` of the platform (the manifest one level up, next to `bin`).
3. Allow tokens for the endpoint, register the MCP client, and create the marketer and approver roles: [Marketing with an AI agent](../../docs/marketing-demo.md#set-it-up) has every step.
4. Connect an agent as the marketer, as described in [Connect an AI agent](../../README.md#connect-an-ai-agent).

## Try it with an agent

Signed in as a marketer, ask the agent:

| Ask | What happens |
| --- | --- |
| "Good morning, what needs my attention in the Electronics store today?" | `get_marketing_overview` and `find_promotion_issues`: the budget, promotions over budget or policy, coupons missing for a coming promotion |
| "Which promotions conflict with Black Friday?" | `find_promotion_conflicts`: stacking and shadowing, with what to change |
| "Why did order CO-10482 not get the WELCOME10 discount?" | `explain_promotion_evaluation`: the rule that failed, in plain words |
| "Activate the Holiday gift guide. Show me the dry run first, then submit it." | `activate_promotion` with `dryRun`: the diff and the warnings; then a pending change for an approver |
| "Generate 5,000 coupons for the welcome offer" | `generate_coupons`: above 1,000 codes it waits for approval, then runs as a background job |

Then, as an approver, open **Configuration → AI agents (MCP) → Agent approvals** in the back office, review the diff and approve it. [Marketing with an AI agent](../../docs/marketing-demo.md) follows a whole week of such routines.

## How to extend

The sample is written to be extended without changes, as a real module should be:

- Models are created with `AbstractTypeFactory` (`DemoPromotion.Create`), so a derived type replaces them everywhere.
- Services are registered in DI, and the methods of `MarketingAnalytics` and `PromotionEvaluator` are `virtual`, so a module adds its own rules in a derived class.
- Tools take their services from DI and use them for every rule, so a replaced service changes every tool.

The [extension sample](../VirtoCommerce.MCP.MarketingExtensionSample/README.md) uses each of these to add a company's finance rules and tools, with unit tests.

## Files

| File | Demonstrates |
| --- | --- |
| [`Tools/MarketingOverviewTools.cs`](Tools/MarketingOverviewTools.cs) | Orientation: one aggregated, opinionated answer instead of raw rows |
| [`Tools/PromotionFinderTools.cs`](Tools/PromotionFinderTools.cs) | Finders: compact rows, page size from `IMcpPageSizeResolver` |
| [`Tools/PromotionAnalysisTools.cs`](Tools/PromotionAnalysisTools.cs), [`Tools/PromotionInsightTools.cs`](Tools/PromotionInsightTools.cs) | Analysis the platform computes and the model cannot |
| [`Tools/PromotionActionTools.cs`](Tools/PromotionActionTools.cs) | Intent-level actions with `dryRun` and `McpChangePreview`; `[McpRequiresApproval]` for what gives money away |
| [`Tools/CouponBulkTools.cs`](Tools/CouponBulkTools.cs), [`Jobs/CouponJobs.cs`](Jobs/CouponJobs.cs) | Bulk tools: `IMcpJobDispatcher`, job progress, `get_job_status` and `cancel_job`; approval with a `Condition` |
| [`Tools/ContentTools.cs`](Tools/ContentTools.cs), [`Data/ContentDemoStore.cs`](Data/ContentDemoStore.cs) | The content toolset: placements, gaps, targeting, publishing with approval only when it goes live today |
| [`Services/MarketingAccess.cs`](Services/MarketingAccess.cs) | `[McpPermission]`, store scopes with `GetMcpPermissionGrant`, refusals with `McpForbiddenException` |
| [`Services/PromotionEvaluator.cs`](Services/PromotionEvaluator.cs) | Promotion evaluation against orders, for simulation and explanation |
| [`Prompts/MarketingPrompts.cs`](Prompts/MarketingPrompts.cs) | Prompts: the marketer's routines as slash commands |
| [`Resources/MarketingResources.cs`](Resources/MarketingResources.cs) | Resources: the discount policy, a playbook and the retail calendar |
| [`Module.cs`](Module.cs) | One-line registration of every tool, prompt and resource class with `AddMcp*FromAssembly` |

The sample keeps its MCP code in the module project to stay small. A real module keeps it in a project of its own, as described in [Where to put the MCP code of a module](../../README.md#where-to-put-the-mcp-code-of-a-module), and calls the Marketing module services instead of `MarketingDemoStore`: `IPromotionSearchService`, `ICouponService`, and `IMarketingPromoEvaluator` for simulation and explanation.

Do not install the sample on a shared or production environment.
