using System.ComponentModel;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Prompts;

[McpServerPromptType]
[McpScope(McpScopes.Admin)]
public static class ContosoPrompts
{
    [McpServerPrompt(Name = "finance_readiness_check")]
    [Description("Before a sale event: check that every promotion has a cost center and stays above the margin floors.")]
    public static string FinanceReadinessCheck(
        [Description("Store id. Leave empty for every store you manage.")] string store_id = null)
    {
        var store = string.IsNullOrEmpty(store_id) ? "all my stores" : $"store {store_id}";

        return $"""
            Check the promotions of {store} against Contoso's finance rules (resource contoso://policies/finance).

            1. Call find_promotion_issues and keep the missing_cost_center and below_margin_floor findings.
            2. Call get_margin_floors to show the floors per category.
            3. For each finding, propose the fix: a cost center with set_promotion_cost_center, or a lower discount with
               change_promotion_discount (that one needs approval).

            Show the findings as a table: promotion, problem, proposed fix. Change nothing until I confirm.
            """;
    }
}
