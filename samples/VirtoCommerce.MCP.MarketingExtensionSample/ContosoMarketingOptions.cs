using System;
using System.Collections.Generic;

namespace VirtoCommerce.MCP.MarketingExtensionSample;

// Contoso's finance rules, bound from the "Contoso:Marketing" configuration section.
public class ContosoMarketingOptions
{
    public const string SectionName = "Contoso:Marketing";

    // Defaults used when the configuration section does not set the rules.
    private const decimal DefaultCostCenterBudgetThreshold = 20_000m;
    private const decimal DefaultLaptopsMaxPercent = 12m;
    private const decimal DefaultMonitorsMaxPercent = 18m;
    private const decimal DefaultAudioMaxPercent = 25m;
    private const decimal DefaultPrintersMaxPercent = 15m;
    private const decimal DefaultNetworkingMaxPercent = 10m;

    // A promotion with a larger discount budget is booked to a cost center.
    public decimal CostCenterBudgetThreshold { get; set; } = DefaultCostCenterBudgetThreshold;

    // The largest percent discount per category that keeps the margin above Contoso's floor.
    public Dictionary<string, decimal> MaxPercentByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Laptops"] = DefaultLaptopsMaxPercent,
        ["Monitors"] = DefaultMonitorsMaxPercent,
        ["Audio"] = DefaultAudioMaxPercent,
        ["Printers"] = DefaultPrintersMaxPercent,
        ["Networking"] = DefaultNetworkingMaxPercent,
    };
}
