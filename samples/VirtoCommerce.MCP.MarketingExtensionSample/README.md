# MCP Marketing Extension Sample

A client project, "Contoso", that extends the [marketing sample](../VirtoCommerce.MCP.MarketingSample/README.md) **without changing it**, the way a partner extends any Virto Commerce module. It is the template for "our company has its own rules": the agent keeps the marketing toolset and gets Contoso's rules and tools on top.

## Features

| Contoso needs | How the extension does it | What the marketer sees |
| --- | --- | --- |
| Every promotion is booked to a finance cost center | `ContosoPromotion : DemoPromotion` with `CostCenter`, registered with `AbstractTypeFactory<DemoPromotion>.OverrideType` | The new tool `set_promotion_cost_center` |
| Large budgets need a cost center; discounts must keep the margin above a floor per category | `ContosoMarketingAnalytics : MarketingAnalytics` overrides `CheckPolicy` and `FindIssues`, registered in place of the original | The sample's own `get_promotion`, `find_promotion_issues`, `get_marketing_overview` and every action preview report `missing_cost_center` and `below_margin_floor` |
| The marketer can look the floors up | A new tool in the same `admin` scope | `get_margin_floors` |
| A routine before sale events | A new prompt | `/finance_readiness_check` |
| The rules in writing for the agent | A new resource | `contoso://policies/finance` |
| Finance can change the thresholds | Options bound from configuration | `Contoso:Marketing` in `appsettings.json` |

Nothing in the marketing sample is changed or copied: the extension uses its models, its services and its tools.

## Setup

1. Install the [marketing sample](../VirtoCommerce.MCP.MarketingSample/README.md) and its setup ([Marketing with an AI agent](../../docs/marketing-demo.md#set-it-up)).
2. Build this project and copy `module.manifest` and the `VirtoCommerce.MCP.MarketingExtensionSample.*` files from `bin/Debug/net10.0` into `modules/VirtoCommerce.MCP.MarketingExtensionSample/bin` of the platform (the manifest one level up, next to `bin`).
3. Optionally change the rules in `appsettings.json`:

    ```json
    "Contoso": {
      "Marketing": {
        "CostCenterBudgetThreshold": 20000,
        "MaxPercentByCategory": { "Laptops": 12, "Monitors": 18, "Audio": 25 }
      }
    }
    ```

4. Restart the platform. The agent lists `set_promotion_cost_center` and `get_margin_floors` with the marketing tools, and the health check reports the Contoso findings.

Try it:

> Are we ready for Black Friday from a finance point of view?

The agent runs `/finance_readiness_check`: the Black Friday promotion has a budget of 120,000 without a cost center, and 20% off laptops is below the 12% margin floor. "Book it to CC-4100" calls `set_promotion_cost_center`; lowering the discount goes through `change_promotion_discount`, which needs approval.

## How it works

[`Module.cs`](Module.cs) has the three extension points, in this order:

```csharp
// 1. Extend the model: every promotion the sample creates is a ContosoPromotion.
AbstractTypeFactory<DemoPromotion>.OverrideType<DemoPromotion, ContosoPromotion>();

// 2. Replace a service: the sample's tools resolve MarketingAnalytics and get Contoso's rules.
serviceCollection.AddTransient<MarketingAnalytics, ContosoMarketingAnalytics>();

// 3. Add tools, prompts and resources to the same MCP scope.
serviceCollection.AddMcpToolsFromAssembly(typeof(Module).Assembly);
```

- **Order.** `module.manifest` declares the dependency on `VirtoCommerce.MCP.MarketingSample`, so the platform initializes the extension after the sample and the registrations of the extension win.
- **Models.** The sample creates promotions with `AbstractTypeFactory` (`DemoPromotion.Create`) and copies them with `MemberwiseClone`, so the derived type and its fields survive every tool that saves or clones a promotion.
- **Services.** `ContosoMarketingAnalytics` calls the base methods and adds to their result, so the sample's rules still apply and a new rule of the sample reaches Contoso without a change.
- **Tools.** New tools reuse the sample's services (`MarketingAccess` for store scopes, `MarketingAnalytics` for the rules) and follow its conventions: `[McpPermission]`, `dryRun` with an `McpChangePreview`, refusals with `McpForbiddenException`. A tool name is unique in a scope, so the extension adds tools under new names and changes existing tools through their services.
- **Testability.** The logic of each tool is a public method that takes the user (`SetCostCenter`, `GetFloors`); the MCP method only passes `context.User` to it.

## Tests

[`VirtoCommerce.MCP.MarketingExtensionSample.Tests`](../VirtoCommerce.MCP.MarketingExtensionSample.Tests) tests the extension the way the platform wires it, on the sample's demo data with a fixed date:

| Test | Checks |
| --- | --- |
| `Store_Should_CreateTheExtendedPromotion_For_EverySeededPromotion` | The model override reaches the data the sample seeds |
| `Clone_Should_KeepTheExtendedField` | The sample's copies keep Contoso's field |
| `CheckPolicy_Should_AddTheCompanyRules_To_TheSampleRules` | The derived service adds rules and keeps the sample's |
| `FindIssues_Should_ReportPromotionsBelowTheMarginFloor_And_WithoutACostCenter` | The health check of the sample reports Contoso's findings |
| `SetCostCenter_*` | Dry run, save, validation, and a store the user does not manage |
| `Explain_Should_TellWhyTheWelcomeCodeDidNotApply_To_TheComplaintOrder` | A behaviour of the sample the extension relies on |
| `ModuleRegistrationTests` | With both modules initialized in platform order, the analytics are Contoso's and the new tools are in the `admin` scope |

```bash
dotnet test samples/VirtoCommerce.MCP.MarketingExtensionSample.Tests
```

Do not install the sample on a shared or production environment.
