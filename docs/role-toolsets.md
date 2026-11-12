# Build an MCP toolset for a role

An agent is as useful as the tools it gets. A toolset built for a role answers the questions that person asks every day, in a few calls, and changes only what that person may change, with the safeguard each change needs. A toolset built from the module's entities (search, get, save for every type) makes the agent assemble each answer itself, burns its context on full objects, and lets it save anything the API can save.

This guide is the method: from the person to the tools, the safeguards, the role, the routines and the tests. The mechanics of each helper are in the [README](../README.md#developer-guide); this guide links to them. The examples come from three toolsets built this way:

| Role | Toolset | Scope |
| --- | --- | --- |
| Marketer | [Marketing sample](../samples/VirtoCommerce.MCP.MarketingSample/README.md) | `admin` |
| IT operator | [System Operations](https://github.com/VirtoCommerce/vc-module-system-operations#ai-agents-mcp) | `admin` |
| Buyer | [UCP](https://github.com/VirtoCommerce/vc-module-ucp) | `ucp` |

## 1. Write the role brief

Start with one page about the person, before any tool. Write it with someone who does the job.

| Question | Why it matters |
| --- | --- |
| **Who** is it, and which scope do they connect to? | A back-office person uses `admin`; a seller, `vendor`; a buyer, `ucp`. The scope decides how they sign in and which tools they can see. |
| **Ten requests**, in their own words: "How is marketing doing?", "Is it our code or Virto's?" | Each request becomes a tool or a prompt. Requests in the person's words become the descriptions the model reads. |
| **Routines**: what they do every Monday, every morning, after an incident | Routines become prompts. |
| **Data they see**: all stores, some stores, their seller, their organization | Decides the permissions and how each tool narrows its data. |
| **Changes they make**, and which of them a second person checks today | Decides the safeguard of each action ([section 3](#3-choose-the-safeguard-for-each-action)). |
| **What must never happen**: a discount above policy, a restart in business hours | Becomes a warning in a preview, an approval condition, or a tool that is not built. |
| **How you will know it works**: minutes saved on the weekly review, incidents found before customers | Becomes the test scenarios ([section 6](#6-test-the-toolset-as-the-role)). |

The IT operator's brief, shortened:

> An IT operator runs the platform. They connect to `/admin/mcp`. They ask "Is the platform healthy?", "Is this error ours or Virto's?", "Are the resources enough?", "Is it ready for production?", "What happened last night?", and they prepare support cases. Every morning they run a health check; after an incident, a timeline. They see the whole platform, never customer data. They reset the cache themselves; a restart is checked by a second person. A support case must never contain a secret.

## 2. Turn the requests into tools

### Shape the toolset

Sort the requests into the five layers of [Design a toolset](../README.md#design-a-toolset): orientation, finders, analysis, intent-level actions, bulk. Then check the result:

- **Every request is answered in one to three calls.** "How is marketing doing?" is `get_marketing_overview`, not `search_promotions` plus `get_promotion` for each of them.
- **Orientation comes first.** One tool a new conversation starts with, and its description says so: "Start here. The state of marketing in the stores the user manages…" (`get_marketing_overview`).
- **Actions are the role's verbs**: `pause_promotion`, `extend_promotion`, `reset_platform_cache`, `restart_platform`. Not `update_promotion` with any field: a verb has one meaning, one permission, one safeguard and one line in the audit log.
- **A toolset stays small.** The layer counts add up to about 10–20 tools. More than that is usually two roles: the marketing sample's 35 tools serve two jobs, promotions (24) and content (11).
- **What is not built is a decision too.** A tool for something that must never happen through an agent is simply not there. System Operations has no tool that changes settings.

### Name and describe each tool for the model

The model chooses tools from their names and descriptions only.

- **Name**: `verb_noun` in snake_case: `find_promotion_conflicts`, `explain_error`, `track_order`. Use the same verbs across modules: `get_` one object, `search_` or `list_` many, `find_` problems, `explain_` a reason, `analyze_` figures.
- **Description**: when to use it, what it returns, and when to use another tool instead. Write it in the words of the brief.
- **Annotations**: set `ReadOnly`, `Destructive` and `Idempotent` on `[McpServerTool]` honestly. Clients use them to decide whether to ask the user before a call.
- **Arguments**: describe each one, with its units and what omitting it means: "Store id. Omit it for every store the user manages."

```csharp
[McpServerTool(Name = "get_marketing_overview", ReadOnly = true, Destructive = false, Idempotent = true)]
[Description("Start here. The state of marketing in the stores the user manages: live, scheduled, draft and paused promotions, " +
    "discount spend this month against the budget, the best promotions of the last 7 days, promotions ending or starting soon, " +
    "the most important issues and the retail events ahead. Use it for a morning check or before planning.")]
public object GetMarketingOverview(
    RequestContext<CallToolRequestParams> context,
    [Description("Store id. Omit it for every store the user manages.")] string store_id = null)
```

### Design the arguments

- **Optional scope arguments**: `store_id`, `seller_id` and the like default to everything the user may see.
- **Lists page**: take `take` and `skip`, turn them into a page with `GetPagingAsync`, and return an [`McpPage<T>`](toolset-helpers.md#paging) with the total and the next skip, so the agent knows there is more. A list without `skip` cuts off silently at `MCP.Tools.MaxTake`: System Operations' `list_modules` returned 50 of 91 modules until it got `skip`.
- **Time windows**: an `hours` or `days` number, or a `since` date, with a default and a cap, read with [`McpTimeWindow`](toolset-helpers.md#time-windows). "The last 24 hours" is what people ask for, not two timestamps.
- **Writes take `dryRun`**: the dry run returns the same preview as the real call, without the change. See [Previews](../README.md#previews).
- **Enumerations as strings**, with the values listed in the description: `"severity": "critical" | "warning" | "info"`.
- **Ids the agent sends are claims.** Check each id against the user's data, as a tool checks a `store_id`. Never pass it on to a service as the id of another object: UCP took a shipment's address id the agent sent as the id of the cart's own address, the cart service failed on two entities with one key, and the cart stayed broken on that instance.

### Shape the answers

- **Compact rows**: an id and the 5–8 fields the decision needs, never the full object. Names, not only ids: an organization by name, a store by name.
- **Units and context**: money with its currency, a percentage with what it is a percentage of, "774 MB of 1024 MB (75.6%)".
- **Reasons, not only results**: an analysis returns findings with evidence and a suggested fix, as [`McpFinding`](toolset-helpers.md#findings): an id, severity, title, evidence, fix and category. The agent turns them into an action list.
- **The next step**: name the tool or the screen that continues the work: "Approve it in Agent approvals", "Call get_job_status with job_id".
- **Say what you cannot see.** A tool that cannot read something answers "not available" with the reason, never zero or "no problems". System Operations read only Hangfire's storage on a platform that runs RabbitMQ jobs, and reported no failed jobs while jobs were failing.
- **Measure what you name.** A figure is compared with the limit it names: the container's memory limit, not the GC heap limit. A false "scale up" costs the person's trust in every other answer.
- **Name the instance.** On a cluster, data held in the memory of an instance (an error journal, counters since start) is per instance; the answer names the instance it came from.

Answers are serialized with snake_case names for every module ([Add tools to a scope](../README.md#add-tools-to-a-scope)).

### Write errors the agent can pass on

- Refuse with a sentence the user understands and can act on: `throw new McpForbiddenException("You do not manage the marketing of store 'fashion'.")`. The call is audited as `Forbidden`.
- Map a failure of another layer to the tool's own error ("The address could not be saved; check the address fields"), not the raw message of the service or the GraphQL error.
- Never put a secret, a connection string or a stack trace in an answer.

### Keep the tools stateless

MCP endpoints are stateless, and a platform often runs several instances. A tool must not rely on what an earlier call left in the memory of one instance. Something that must happen once across the cluster, such as a one-time token, is remembered in the database or the distributed cache, not in `IMemoryCache`.

## 3. Choose the safeguard for each action

Every action of the brief gets the lightest safeguard that matches its risk:

| The action | Safeguard | Example |
| --- | --- | --- |
| Reads | None; the permission is enough | `get_marketing_overview`, `check_capacity` |
| Lowers risk and is easy to undo | None, or [confirmation in the chat](../README.md#confirmation-in-the-chat) | `pause_promotion` asks the user, then pauses at once |
| Changes something at once, the user's own call | [Confirmation in the chat](../README.md#confirmation-in-the-chat) | `reset_platform_cache` |
| Costs money, affects customers, or is hard to undo | [Approval](../README.md#approval) by a second person: `[McpRequiresApproval]` | `extend_promotion`, `schedule_promotion`, `restart_platform` |
| Only some calls are risky | [Approval with a condition](../README.md#approval-conditions) on the arguments | `generate_coupons`: one code runs at once, more than 1000 wait for approval |
| Many objects at once | A [bulk tool and a job](../README.md#bulk-tools-and-jobs), with a preview of the first items; approval when the change is risky | `deactivate_stale_coupons` |
| Must never happen through an agent | No tool | Changing settings in System Operations |

- Confirmation guards the user against the agent's mistake; approval guards the business against the user's. A client answers a confirmation itself, so confirmation is never the only control on a change that needs a second person.
- Every write returns a [preview](../README.md#previews): the dry run, the approver and the audit see the same diff. Put what must never happen into the preview's warnings: "The discount is above the 30% policy".
- Mark personal data in arguments with [`[McpSensitive]`](../README.md#sensitive-arguments), so it shows as `***` in the audit log and in approvals.

## 4. Give the role its permissions

Agents have no permissions of their own: an agent sees the tools whose permissions its user has. So the role is an ordinary platform role, made of the permissions the tools check.

1. **Protect each tool with the permission of what it does**, the same one the back office and the REST API check: `marketing:read` to read promotions, `marketing:update` to change them, `cache:reset` to reset the cache. A tool that does something no permission covers gets a new permission of its module; System Operations added `systemoperations:diagnostics:read`.
2. **Narrow the data in the tool** when a permission can be limited to stores or sellers: use `[McpPermission]` and check every argument and every object loaded by id. See [Permissions limited to stores or sellers](../README.md#permissions-limited-to-stores-or-sellers).
3. **Write the role down** with its permissions and why each one is there, for example the IT operator's:

    ```json
    {
      "name": "IT Platform Operator",
      "description": "Runs the platform with an AI agent over /admin/mcp: System Operations diagnostics, cache reset, a restart as a request for approval, and the MCP audit log.",
      "permissions": [
        { "name": "systemoperations:diagnostics:read" },
        { "name": "cache:reset" },
        { "name": "platform:module:manage" },
        { "name": "mcp:audit:read" }
      ]
    }
    ```

    Create it in **Security → Roles**, or with `PUT /api/platform/security/roles`. A role with scoped permissions is saved with global permissions on its first save: save it once more after creating it, until this is fixed in the platform.
4. **Keep approvers apart.** The person who asks for a change and the person who approves it are different people: give `mcp:approve` (or the scope's approver permission) to another role, and keep `MCP.Approval.AllowSelfApproval` off. The IT operator above can ask for a restart but not approve it.
5. **Add the back-office permissions the role needs to follow its agent**: `mcp:audit:read` for **Agent audit log**; `mcp:manage` only for whoever switches tools off and disconnects other people's agents.
6. **Register one OAuth client per kind of agent, not per role.** "Claude Code" or "ChatGPT" is a client; the role comes from the user who signs in. See [Set up the platform for agents](../README.md#set-up-the-platform-for-agents).

## 5. Add prompts and resources for the routines

A routine of the brief, such as a weekly review, is a prompt: a template the client offers as a slash command, `/weekly_marketing_review` in Claude. A prompt holds the know-how of the role, which tools to call in which order and what to report; the tools still decide what the user may see and do.

```csharp
[McpServerPrompt(Name = "weekly_marketing_review")]
[Description("Monday review of promotions: what runs, what needs attention, and a short action list.")]
public static string WeeklyMarketingReview(
    [Description("Store id. Leave empty for every store you manage.")] string store_id = null)
{
    return $"""
        Run my weekly marketing review.

        1. Call get_marketing_overview.
        2. Call find_promotion_issues and find_promotion_conflicts.
        3. Call analyze_promotion_performance with last_days 7.

        Then write the review for me:
        - Three numbers first: discount spend this month against the budget, active promotions, open high-severity issues.
        - What needs action: at most five items, most urgent first, each with the action you propose and the tool you would use.

        Do not change anything. Ask me which actions to take; changes that need approval are submitted for approval.
        """;
}
```

- **Number the tool calls**, name the tools exactly, and say what to report and in which order.
- **Say what not to do**: a review prompt changes nothing and asks before any action.
- **Prefer optional arguments.** When a required argument is missing, `prompts/get` is answered today with `-32603 An error occurred.` instead of an error that names the argument. Clients that read `prompts/list` pass required arguments, but a person typing the command may not.
- **Put reference material in resources**: the discount policy, the playbook for a sale event (`marketing://policies/discounts`, `marketing://playbooks/sale-event` in the marketing sample). A prompt tells the agent to read them, and the business changes them without a release.

System Operations' prompts follow the same pattern: `daily_health_check`, `production_readiness`, `capacity_review`, `incident_rca`, `is_it_us_or_virto`, `prepare_support_case`.

## 6. Test the toolset as the role

### Unit tests

[`VirtoCommerce.MCP.Testing`](toolset-helpers.md#the-testing-package) calls the tools in-process as a user with given permissions, and checks the whole toolset against the conventions of this guide in one test. For each tool:

- it is visible with its permission and hidden without it;
- a scoped user gets only the data of their stores or sellers, and an id of another store is refused;
- the dry run returns the preview and changes nothing; the real call changes what the preview said;
- an approval condition sends the risky arguments to approval and runs the others;
- a list pages: two pages together hold every item once.

### Scenarios

Turn the ten requests of the brief into a table, and run it with a real agent, signed in as a user who has only the role, never as an administrator:

| # | The person asks | Expected tools | Expected result |
| --- | --- | --- | --- |
| 1 | What can I do here? | `tools/list`, `prompts/list` | The role's tools and prompts; nothing the role does not allow |
| 2 | Run the daily health check | prompt `daily_health_check` | Health, new errors, failed jobs, capacity and configuration, with an action list |
| 3 | Restart the platform | `restart_platform` | Not restarted: a change id; another person rejects or approves it in **Agent approvals** |
| 4 | (an approver) Show me what the operator did | **Agent audit log** | Every call with the user, the client and the outcome |

Keep a second table for what the role must not be able to do: a tool of another role, a store the user does not manage, a change the user tries to approve themselves.

### On a test environment

Run the scenarios on an environment that looks like production, because some defects only show there:

- **Several instances**: state that one instance keeps in memory is unknown to the others.
- **The real infrastructure**: the background job engine, Redis, the search provider of the environment, not the developer's defaults.
- **A container with a memory limit**: runtime figures differ inside a container.
- **Real sign-in**: the agent's own OAuth flow through the browser, the consent page, a token for the exact MCP resource.

Script what a deploy must keep working (modules installed without errors, sign-in, a call of each layer) and run it after every deploy. When handing the toolset to QA, write down what is already configured on the environment, how to set it up from scratch, and the scenarios.

## Checklist

Before a role's toolset ships:

- [ ] The brief exists, and every one of its requests is answered in one to three calls.
- [ ] One orientation tool says "Start here".
- [ ] Actions are the role's verbs; each has `dryRun`, returns a preview, and has the safeguard of its risk.
- [ ] Every tool has a permission; tools with scoped permissions narrow the data, by every argument and every id.
- [ ] Lists page with `take` and `skip` and return the total; no answer is cut off silently.
- [ ] Answers are compact, with units, reasons and the next step; "not available" is never reported as zero.
- [ ] Errors are sentences the user can act on, with no internals and no secrets.
- [ ] Personal data in arguments is `[McpSensitive]`.
- [ ] The role is written down with its permissions; approvers are another role.
- [ ] The routines are prompts, with optional arguments where possible; reference material is in resources.
- [ ] The scenarios pass as the role, on an environment with several instances and its real infrastructure.
