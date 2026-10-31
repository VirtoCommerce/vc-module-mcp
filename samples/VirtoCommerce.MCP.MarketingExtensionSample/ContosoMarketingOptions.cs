using System;
using System.Collections.Generic;

namespace VirtoCommerce.MCP.MarketingExtensionSample;

// Contoso's finance rules, bound from the "Contoso:Marketing" configuration section.
public class ContosoMarketingOptions
{
    public const string SectionName = "Contoso:Marketing";

    // A promotion with a larger discount budget is booked to a cost center.
    public decimal CostCenterBudgetThreshold { get; set; } = 20_000m;

    // The largest percent discount per category that keeps the margin above Contoso's floor.
    public Dictionary<string, decimal> MaxPercentByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Laptops"] = 12m,
        ["Monitors"] = 18m,
        ["Audio"] = 25m,
        ["Printers"] = 15m,
        ["Networking"] = 10m,
    };
}
