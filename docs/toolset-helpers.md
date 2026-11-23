# Helpers and tests for MCP toolsets

Building blocks for the tools of a module, in `VirtoCommerce.MCP.Core`, and a test package, `VirtoCommerce.MCP.Testing`, that calls the tools as a user and checks a toolset against the conventions. [Build an MCP toolset for a role](role-toolsets.md) explains why each one exists; this page shows how to use them.

| Need | Use | Package |
| --- | --- | --- |
| A list tool that never cuts its answer off silently | [`McpPage<T>`, `GetPagingAsync`](#paging) | `VirtoCommerce.MCP.Core` |
| Analysis results that read the same in every module | [`McpFinding`](#findings) | `VirtoCommerce.MCP.Core` |
| A link to the screen for what the tools do not cover | [`IMcpLinkResolver`, `WithLink`](#links-to-screens) | `VirtoCommerce.MCP.Core` |
| "The last 24 hours", "since Monday" | [`McpTimeWindow`](#time-windows) | `VirtoCommerce.MCP.Core` |
| A setup that other modules plug parts into, such as a store's | [`McpSetupPartPlan`, `McpSetupChoice` and the others](#setups-that-modules-plug-into) | `VirtoCommerce.MCP.Core` |
| Describe settings as the back office does, without secrets | [`IMcpSettingTexts`, `IMcpSecretSettings`](#describe-settings) | `VirtoCommerce.MCP.Core` |
| Call tools in a unit test as a user with given permissions | [`McpToolHarness`, `McpTestUser`](#call-tools-as-a-user) | `VirtoCommerce.MCP.Testing` |
| Check a toolset against the conventions in one test | [`McpToolsetConventions`](#check-the-conventions) | `VirtoCommerce.MCP.Testing` |

## Paging

A list tool takes `skip` and `take`, and answers with one page and the total. `GetPagingAsync` turns the two optional arguments into an `McpPaging`: a negative `skip` reads as 0, and `take` goes through `IMcpPageSizeResolver`, so it never exceeds `MCP.Tools.MaxTake`.

```csharp
[McpServerTool(Name = "search_promotions", ReadOnly = true, Destructive = false, Idempotent = true)]
[Description("Search promotions of the stores the user manages. Returns one page of compact rows and the total.")]
[McpPermission("marketing:read")]
public async Task<McpPage<PromotionRow>> SearchPromotions(
    IMcpPageSizeResolver pageSizeResolver,
    IPromotionSearchService promotionSearchService,
    [Description("Words to find in the name or description.")] string keyword = null,
    [Description("Rows to skip, from 0. Use next_skip of the previous page.")] int? skip = null,
    [Description("Rows to return. Omit it for the default page size.")] int? take = null)
{
    var paging = await pageSizeResolver.GetPagingAsync(skip, take);
    var criteria = new PromotionSearchCriteria { Keyword = keyword }.WithPaging(paging);
    var result = await promotionSearchService.SearchAsync(criteria);

    return result.ToMcpPage(paging, PromotionRow.From);
}
```

The agent receives:

```json
{
  "items": [ { "id": "autumn-laptops", "name": "Autumn laptop sale", "status": "active" } ],
  "total_count": 16,
  "skip": 0,
  "take": 1,
  "next_skip": 1,
  "has_more": true
}
```

- `next_skip` is the `skip` of the next page, and is null on the last one; `has_more` says the same in one word.
- `ToMcpPage` maps a module's `GenericSearchResult<T>` or a list already in memory; `McpPage<T>.FromList` pages an in-memory list; `McpPage<T>.Create` takes the items and the total as they are.
- `WithPaging` sets `Skip` and `Take` of a module's `SearchCriteriaBase`.

## Findings

An analysis tool (a health check, a conflict check, a readiness check) returns `McpFinding` items. The same shape in every module lets an agent merge the findings of several tools into one action list.

| Property | Meaning |
| --- | --- |
| `id` | A stable id of the kind of finding, such as `budget-almost-spent`. Tests and agents refer to it; it does not change with the wording |
| `severity` | `critical`, `warning` or `info` (`McpFindingSeverities`) |
| `title` | The finding in one sentence a person understands |
| `why` | Why it matters, when the title does not say: what it costs or puts at risk. Optional |
| `evidence` | The figures, with units and the limit they are compared with: "Spent 8,700 of 10,000 USD (87%)" |
| `fix` | What to do, naming the tool or the screen that does it |
| `screen` | Where in the back office it is fixed, such as "Stores > EU store > Payment methods". Optional; [`IMcpLinkResolver`](#links-to-screens) gives its link |
| `manual_only` | True when only a person can fix it, never a tool, such as a secret to enter in the back office. Written only when true |
| `category` | The area or the cause, as the module defines it, such as `policy` or `infrastructure` |
| `object_type`, `object_id`, `object_name` | The object the finding is about |

```csharp
var findings = new List<McpFinding>();

if (spent >= budget * 0.85m)
{
    findings.Add(new McpFinding
    {
        Id = "budget-almost-spent",
        Severity = McpFindingSeverities.Warning,
        Title = $"The budget of '{promotion.Name}' is almost spent",
        Why = "The promotion stops when the budget runs out.",
        Evidence = $"Spent {spent:N0} of {budget:N0} {currency} ({spent / budget:P0}).",
        Fix = "Pause it with pause_promotion, or raise the budget.",
        Category = "budget",
        ObjectType = "promotion", ObjectId = promotion.Id, ObjectName = promotion.Name,
    });
}

return new { findings = findings.OrderBySeverity(), highest = findings.GetHighestSeverity() };
```

`OrderBySeverity` puts critical first and keeps the order within a severity; `CountBySeverity` counts one severity. A property left null, such as `why` or `screen`, is left out of the answer.

## Links to screens

A toolset does not cover every field. For the rest, the answer names the screen that does it, with a link the person opens. `IMcpLinkResolver.GetLinkAsync` gives the link: the public origin of the request's scope, as the scope's `IMcpPublicOriginResolver` gives it, followed by the route of the screen. `WithLink` writes the screen with its link in brackets.

```csharp
// A tool or a service of the toolset, given IMcpLinkResolver linkResolver
var link = await linkResolver.GetLinkAsync("/#!/workspace/store");
var fix = $"Back office: {"Stores > EU store > Payment methods".WithLink(link)}.";
// Back office: Stores > EU store > Payment methods (https://admin.example.com/#!/workspace/store).
```

- The link is null outside an MCP request, when the scope has no public origin, and when the classic back office (the AngularJS admin, app `platform`) is not installed or the platform's admin UI is switched off (`VirtoCommerce:PlatformUI:Enable`). `WithLink` then gives the screen alone, so the answer still names it.
- The route is the screen's address in the classic back office, which starts with `/#!/workspace/`; the Settings screen takes `?setting=<name>` to open one setting, as `explain_setting` does. The link follows where the platform serves the classic back office (`IAdminUIAppService`): at the site root, or at `/apps/platform/` when another admin UI, such as Admin Shell, owns the root (`VirtoCommerce:PlatformUI:RootApp`).
- `GetAppLinkAsync(appId, route)` links a screen of another admin UI app the same way, by the app's id: the MCP module links the Admin Shell (`admin-shell`), where its own screens are. The link is null outside an MCP request, when the scope has no public origin, and when no installed admin UI has the id. The link stays when the platform's admin UI is switched off: only the classic back office refuses its page then, and the platform still serves the other apps at `/apps/{appId}/`.
- A toolset registers nothing for it: the MCP module, which serves the toolset, registers `IMcpLinkResolver`. Only a service that also runs without the MCP module installed needs its own `TryAddTransient<IMcpLinkResolver, McpLinkResolver>()`; its links are null there, outside any MCP request. A solution replaces it to send the links elsewhere.

## Time windows

`McpTimeWindow` reads the period people ask about from one argument, applies a default when the agent gives none, and caps it, so one call cannot scan years of data. The window always ends now and is in UTC.

```csharp
[McpServerTool(Name = "search_errors", ReadOnly = true, Destructive = false, Idempotent = true)]
[Description("Errors of the last hours, grouped by fingerprint.")]
[Authorize("systemoperations:diagnostics:read")]
public object SearchErrors(
    TimeProvider timeProvider,
    IErrorJournal journal,
    [Description("Hours to look back, 24 by default, at most 168.")] int? hours = null)
{
    var window = McpTimeWindow.LastHours(hours, timeProvider, defaultHours: 24, maxHours: 168);
    var errors = journal.GetErrors().Where(x => window.Contains(x.Time)).ToList();

    return new { window, errors };
}
```

| Factory | Argument | Default | Cap |
| --- | --- | --- | --- |
| `LastHours(hours, timeProvider, defaultHours, maxHours)` | `int? hours` | 24 hours | 30 days |
| `LastDays(days, timeProvider, defaultDays, maxDays)` | `int? days` | 7 days | 365 days |
| `Since(since, timeProvider, defaultLength, maxLength)` | `DateTime? since` | as given | as given |

- The window serializes as `from`, `to`, `hours` and `was_capped`: return it with the answer, so the agent says what period it covered and that it was shortened.
- `hours` or `days` below 1, and a `since` in the future, are refused with an `McpException` whose message the agent can pass on.
- A `since` without a kind is read as UTC; a local time is converted.
- Take `TimeProvider` from DI, so tests can fix the time.

## Setups that modules plug into

Some setups span modules. A store's setup takes the basics from Store, the payment methods from Payment, the shipping methods from Shipping and the tax providers from Tax. The module that owns the setup defines its contract: the interfaces, the context and the service. Each module then adds a part. These shapes are what the parts exchange, in any such contract:

| Type | What it is |
| --- | --- |
| `McpSetupChoice` | An allowed value of a section field, such as the code of a shipping method, with the settings it takes |
| `McpSetupSetting` | A setting with the value of the object being set up, and whether a setup can change it. It derives `McpSetting` ([Describe settings](#describe-settings)); a secret is described without its values |
| `McpSetupPartPlan` | What applying a part's section would change (`McpFieldChange` items), why it cannot be applied, and what the approver should know. A part derives it to carry the state after the change. `RequiredPermissions` names the permissions the plan needs when they follow what it does, as the catalog part's do (`RequirePermission` adds one). Null leaves the part's `Create` or `Update` permission; an empty list checks no permission, so it suits only a plan that reads and changes nothing |
| `McpSetupPartResult`, `McpSetupPartStatus` | What a setup changes or changed in one part, and how it went: `NoChange`, `WillChange`, `Applied`, `Failed` or `NotApplied`; with the permissions its plan needs (`RequiredPermissions`) |
| `McpSetupPermissions` | The existing permissions that guard a part: `Read`, `Update`, `Create`, and the permission scope that limits them (`ScopeType`) |
| `IMcpSetupValues` | Compares a value from a request with a stored one or a default: numbers and booleans by value when either side is one (`10` equals `"10.0"`), two texts by their exact text (`"007"` and `"7"` differ), as the platform compares setting values; an overload takes a `StringComparison`, such as `OrdinalIgnoreCase` to find an allowed value sent in another case. A service: `AddMcpSettingRules` registers the MCP module's `McpSetupValues`, and a solution replaces it for every setup |
| `McpFinding` | What the object is missing, with `why` and `screen` ([Findings](#findings)) |

The rules belong to the contract, not to these shapes: what refuses a request, the order of the parts, what blocks going live. Store's setup is the example: its parts and their rules are in the Store module's README, under "A module's part of a store's setup".

## Describe settings

A tool that describes settings, such as `explain_setting`, shows them as the back office does and never shows a secret. The same helpers serve every toolset:

- `McpSetting` is a setting described for a person: its name, title, description, module, type, default, value and allowed values, whether it is required and whether its value was changed. A toolset derives it to add what it knows, as `McpSetupSetting` does for a setup.
- `IMcpSettingTexts` gives a setting's title and description: `GetTranslation` reads the texts once for an answer, and `Find` looks a setting up in them. The MCP module's `McpSettingTexts` reads the modules' English localization files, as the back office shows them. A title the files do not have is the setting's display name, or else its name; such a description is null.
- `IMcpSecretSettings.IsSecret` is the rule that keeps secrets out, the same one the settings tools use. The MCP module's `McpSecretSettings` makes a setting a secret when it is typed `SecureString`, when it is a setting of a Virto Commerce module known to be a secret (`McpKnownSecretSettings`), when a module names it with `AddMcpSecretSettings`, or when its name has the whole word Key, Secret, Password, Token or Credential, also as a plural or followed by digits: `ApiKey`, `ApiKeys` and `ApiKey2` do, `Keyword` does not. A secret is never listed and its value is never returned, so an agent never proposes to send one.
- Every toolset shares the two rules, and every setup the comparison of values (`IMcpSetupValues`), so a solution changes one for all of them by registering its own. `AddMcpSettingRules` registers the MCP module's three; a module whose services use them without the MCP module calls it too.

```csharp
// Module.Initialize: the module's own secrets that the rule does not find, and the rules for its services
serviceCollection.AddMcpSecretSettings("MyModule.SigningCode");
serviceCollection.AddMcpSettingRules();

// The service that describes the settings, given IMcpSettingTexts and IMcpSecretSettings
var translation = settingTexts.GetTranslation();
var (title, description) = settingTexts.Find(translation, descriptor.Name, descriptor.DisplayName);
var isSecret = secretSettings.IsSecret(descriptor.Name, descriptor.ValueType);

// A solution's own rule, for every toolset: registered in its module's Initialize
serviceCollection.AddSingleton<IMcpSecretSettings, MySecretSettings>();
```

## The testing package

Add `VirtoCommerce.MCP.Testing` to the test project of the module, at the version of `VirtoCommerce.MCP.Core` the module references:

```xml
<PackageReference Include="VirtoCommerce.MCP.Testing" Version="3.1001.0" />
```

It depends only on `VirtoCommerce.MCP.Core` and the MCP SDK, and works with any test framework: it throws on failure and returns plain results.

### Call tools as a user

`McpToolHarness` registers the module's tools with `AddMcpCapabilities`, as the module does, and calls them in-process as a given user. No platform, database or HTTP server is needed: the test registers the services the tools use.

```csharp
public class PromotionToolsTests : IAsyncDisposable
{
    private readonly FakePromotionStore _store = new();
    private readonly McpToolHarness _harness;

    private static readonly ClaimsPrincipal _marketer =
        McpTestUser.Create("marketer").WithPermissions("marketing:read", "marketing:update");

    private static readonly ClaimsPrincipal _fashionMarketer =
        McpTestUser.Create("fashion").WithScopedPermission("marketing:read", "MarketingStoreSelectedScope", "fashion");

    public PromotionToolsTests()
    {
        // The services the tools take from DI: the module's own, faked, and the MCP module's page size.
        var pageSize = new Mock<IMcpPageSizeResolver>();
        pageSize.Setup(x => x.GetPageSizeAsync(It.IsAny<int?>())).ReturnsAsync((int? take) => Math.Clamp(take ?? 20, 1, 50));

        _harness = McpToolHarness.Create<McpAssemblyMarker>(services => services
            .AddSingleton<IPromotionStore>(_store)
            .AddSingleton(pageSize.Object));
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public void AMarketer_SeesTheMarketingTools()
    {
        Assert.Equal(["extend_promotion", "pause_promotion", "search_promotions"], _harness.GetVisibleToolNames(_marketer));
    }

    [Fact]
    public async Task ExtendPromotion_WaitsForApproval_AndChangesNothing()
    {
        var result = await _harness.CallAsync(_marketer, "extend_promotion", new { promotion_id = "autumn-laptops", days = 7 });

        Assert.Equal(McpToolCallOutcome.PendingApproval, result.Outcome);
        Assert.Contains("Estimated extra discount cost", result.Read<McpChangePreview>().Warnings.Single());
        Assert.Equal(new DateTime(2026, 10, 31), _store.Get("autumn-laptops").EndDate);
    }

    [Fact]
    public async Task AStoreTheMarketerDoesNotManage_IsForbidden()
    {
        var result = await _harness.CallAsync(_fashionMarketer, "search_promotions", new { store_id = "electronics" });

        Assert.Equal(McpToolCallOutcome.Forbidden, result.Outcome);
    }
}
```

**`McpTestUser`** builds the claims the platform issues: permissions granted globally (`WithPermissions`), permissions limited by a permission scope (`WithScopedPermission`, read by `GetMcpPermissionGrant`), the administrator role (`AsAdministrator`), and anonymous users (`McpTestUser.Anonymous()`).

**What a user sees.** `GetVisibleToolNames(user)` and `IsVisible(tool, user)` apply the rules of the MCP module:

- a scope that requires a signed-in user serves only tools that say who may use them (`[Authorize]`, `[McpPermission]`, `[McpAnyUser]`, `[AllowAnonymous]`), and only to a signed-in user;
- `[Authorize("x")]` needs the permission granted globally or the administrator role; `[McpPermission("x")]` also accepts a grant limited by scopes;
- `[McpApproverOnly]` needs the scope's approver permission;
- a name in `DisabledTools` (or `*`) switches the tool off, as `MCP.Tools.Disabled` does.

**What a call ends with.** `CallAsync(user, tool, arguments)` takes the arguments as an object or a dictionary, with the names the tool declares, and returns an `McpToolCallResult`:

| `Outcome` | When |
| --- | --- |
| `Succeeded` | The tool ran and answered |
| `PendingApproval` | The tool requires approval for these arguments (`[McpRequiresApproval]`, with its `Condition`): it ran as a dry run, and the result is the preview an approver sees |
| `Declined` | The tool asks for confirmation and the test declined it (`DeclineConfirmation`); it did not run |
| `Forbidden` | The tool threw `McpForbiddenException`; `Error` is its message |
| `Failed` | The tool threw `McpException` or answered with an error, or the call named an argument the tool does not take, which the MCP module refuses; `Error` is the message |
| `NotAvailable` | The user may not see the tool |
| `NotFound` | No tool of that name in the scope |

- `Read<T>()` reads the answer with the names the agent sees (`McpSerialization.Options`, snake_case); `Json` and `Text` give it raw.
- `AskedForConfirmation` and `WasDryRun` say what happened on the way; `EnsureSucceeded()` throws with the outcome and the message otherwise.
- `McpToolCallOptions.AsApprovedChange` runs a call that needs approval for real, as an approved change does; `DeclineConfirmation` answers a confirmation with **Decline** (by default the harness confirms).
- The scope is the admin scope (signed-in users, approval with `mcp:approve`); pass another `McpScope` to `Create` for a scope of your own. Tools a test registers itself go through `McpToolHarness.Create(services => services.AddMcpTools<MyTools>(), configureServices, scope)`.
- During a call, `IHttpContextAccessor.HttpContext` carries the user and the scope, as on the MCP endpoint, and `IMcpLinkResolver` gives links from the scope's `IMcpPublicOriginResolver`, which the test registers.

The harness reproduces the rules above; it does not run the module's call filters, write the audit, or store pending changes. Test those through the MCP module's endpoint, as a [scenario on a test environment](role-toolsets.md#on-a-test-environment).

### Check the conventions

One test checks every tool of the module against the conventions of [Build an MCP toolset for a role](role-toolsets.md), the way the agent will see them:

```csharp
[Fact]
public void Toolset_FollowsTheConventions()
{
    McpToolsetConventions.Verify<McpAssemblyMarker>();
}
```

`Verify` throws `McpConventionException` listing every violation as `tool [rule]: what to do`. `Check` returns them instead, and both take an assembly, a marker type, or a list of tool classes.

| Rule | Checks | Why |
| --- | --- | --- |
| `name` | `Name` is set on `[McpServerTool]`, in snake_case `verb_noun` | The name is the tool's contract with agents |
| `unique-name` | One tool of a name in a scope | The MCP module serves only one |
| `description` | The tool has a `[Description]` | The model chooses tools by their descriptions |
| `argument-description` | Every argument of a simple type has a `[Description]`, except `dryRun` | The model fills arguments by their descriptions |
| `scope` | `[McpScope]` on the tool or its class | A tool without a scope is not served |
| `access` | `[Authorize]`, `[McpPermission]`, `[McpAnyUser]` or `[AllowAnonymous]` | A scope that requires a signed-in user does not serve a tool without it |
| `read-only` | `get_`, `search_`, `list_`, `find_`, `explain_`, `analyze_`, `describe_`, `check_` and `track_` tools set `ReadOnly = true`; a read-only tool does not ask for approval or confirmation | Clients call read-only tools without asking the user |
| `dry-run` | A tool that is not read-only takes a `bool dryRun` | The dry run is the preview; the MCP module refuses calls of an approval tool without it |
| `safeguard` | A destructive tool has `[McpRequiresApproval]` or `[McpRequiresConfirmation]` | The [risk ladder](role-toolsets.md#3-choose-the-safeguard-for-each-action); set `Destructive = false` for a tool that only adds |
| `paging` | `search_` and `list_` tools take `int? skip` and `int? take` | No answer is cut off silently |
| `approval-condition` | The `Condition` of `[McpRequiresApproval]` is a public static bool method of the tool class taking the arguments | Otherwise every call needs approval |
| `confirmation-message` | Every `{argument}` of the `[McpRequiresConfirmation]` message is an argument of the tool | Otherwise the question shows the placeholder |

An exception is written down where it is made, with its reason:

```csharp
McpToolsetConventions.Verify<McpAssemblyMarker>(x => x
    .Ignore(McpConventionRules.Safeguard, "unpublish_content", "Takes a publication down at once: it lowers risk, as pausing a promotion does.")
    .Ignore(McpConventionRules.Paging, "list_customer_segments", "A store has five segments."));
```

A tool name of `*` ignores a rule for the whole toolset. The marketing samples run this check: [`ToolsetConventionsTests`](../samples/VirtoCommerce.MCP.MarketingExtensionSample.Tests/ToolsetConventionsTests.cs).
