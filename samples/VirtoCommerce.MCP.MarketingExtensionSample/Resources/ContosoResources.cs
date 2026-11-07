using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Resources;

[McpServerResourceType]
[McpScope(McpScopes.Admin)]
public class ContosoResources(IOptions<ContosoMarketingOptions> options)
{
    [McpServerResource(UriTemplate = "contoso://policies/finance", Name = "contoso_finance_policy", MimeType = "text/markdown")]
    [Description("Contoso's finance rules for promotions: cost centers and margin floors. They add to the discount policy.")]
    public string FinancePolicy()
    {
        var floors = string.Join("\n", options.Value.MaxPercentByCategory.Select(x => $"| {x.Key} | {x.Value}% |"));

        return $"""
            # Contoso finance policy for promotions

            These rules add to the discount policy (marketing://policies/discounts).

            ## Cost centers
            - A promotion with a discount budget above {options.Value.CostCenterBudgetThreshold:N0} is booked to a cost center (CC- and four digits).
            - Set it before the promotion is scheduled.

            ## Margin floors
            A percent discount on these categories stays at or below:

            | Category | Max percent off |
            | --- | --- |
            {floors}
            """;
    }
}
