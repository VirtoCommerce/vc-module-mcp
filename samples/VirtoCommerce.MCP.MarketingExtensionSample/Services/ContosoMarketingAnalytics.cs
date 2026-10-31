using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.MarketingExtensionSample.Models;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Services;

// Adds Contoso's finance and margin rules to the marketing sample's analysis. Registered in place of
// MarketingAnalytics, so the sample's own tools (get_promotion, find_promotion_issues, the previews of every action)
// apply the rules without a change to the sample.
public class ContosoMarketingAnalytics(
    MarketingDemoStore store,
    IOptions<ContosoMarketingOptions> options)
    : MarketingAnalytics(store)
{
    private readonly MarketingDemoStore _store = store;

    public const string MissingCostCenterCode = "missing_cost_center";
    public const string BelowMarginFloorCode = "below_margin_floor";

    public override IList<string> CheckPolicy(DemoPromotion promotion)
    {
        var warnings = base.CheckPolicy(promotion);

        if (NeedsCostCenter(promotion))
        {
            warnings.Add($"Contoso finance: a discount budget above {options.Value.CostCenterBudgetThreshold:N0} is booked to a cost center; set it with set_promotion_cost_center.");
        }

        foreach (var (category, maxPercent) in GetMarginFloorBreaches(promotion))
        {
            warnings.Add($"Contoso margin floor: {promotion.RewardValue}% off {category} is below the margin floor; at most {maxPercent}% is allowed.");
        }

        return warnings;
    }

    public override IList<PromotionIssue> FindIssues(IEnumerable<DemoPromotion> promotions)
    {
        var list = promotions.ToList();
        var issues = base.FindIssues(list).ToList();
        var now = _store.Now;

        foreach (var promotion in list.Where(x => x.GetStatus(now) is PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Draft))
        {
            if (NeedsCostCenter(promotion))
            {
                issues.Add(new PromotionIssue(SeverityMedium, MissingCostCenterCode, promotion.Id, promotion.Name, promotion.StoreId,
                    $"Discount budget {promotion.DiscountBudget:N0} without a cost center.", "Set it with set_promotion_cost_center."));
            }

            foreach (var (category, maxPercent) in GetMarginFloorBreaches(promotion))
            {
                issues.Add(new PromotionIssue(promotion.GetStatus(now) == PromotionStatus.Active ? SeverityHigh : SeverityMedium, BelowMarginFloorCode,
                    promotion.Id, promotion.Name, promotion.StoreId,
                    $"{promotion.RewardValue}% off {category} goes below the margin floor of {maxPercent}%.",
                    $"Lower the discount to {maxPercent}% with change_promotion_discount."));
            }
        }

        return issues
            .OrderBy(x => x.Severity switch { SeverityHigh => 0, SeverityMedium => 1, _ => 2 })
            .ThenBy(x => x.StoreId)
            .ToList();
    }

    public bool NeedsCostCenter(DemoPromotion promotion)
    {
        return promotion.DiscountBudget > options.Value.CostCenterBudgetThreshold &&
            string.IsNullOrEmpty((promotion as ContosoPromotion)?.CostCenter);
    }

    public IEnumerable<(string Category, decimal MaxPercent)> GetMarginFloorBreaches(DemoPromotion promotion)
    {
        if (promotion.RewardType != RewardType.PercentOff || promotion.TargetType != TargetType.Category)
        {
            return [];
        }

        return promotion.TargetCategories
            .Where(category => options.Value.MaxPercentByCategory.TryGetValue(category, out var maxPercent) && promotion.RewardValue > maxPercent)
            .Select(category => (category, options.Value.MaxPercentByCategory[category]))
            .ToList();
    }
}
