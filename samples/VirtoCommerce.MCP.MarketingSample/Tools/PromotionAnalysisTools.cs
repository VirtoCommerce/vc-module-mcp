using System;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// Layer 3, analysis: answers the platform computes cheaply and a model cannot, such as budget burn against the plan,
// stacking discounts, or translations missing for a store language. These make the agent feel like an expert.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[McpPermission(MarketingSampleConstants.ReadPermission)]
public class PromotionAnalysisTools(
    MarketingDemoStore store,
    MarketingAccess access,
    MarketingAnalytics analytics)
{
    [McpServerTool(Name = "analyze_promotion_performance", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Results of promotions: redemptions, revenue, discount cost, revenue per unit of discount, order value against the " +
        "store average, budget used, the trend of the last 7 days against the 7 before, and a verdict (strong, ok, weak). " +
        "With promotion_id: one promotion with its daily results. Without it: a ranking of the promotions of the stores.")]
    public object AnalyzePromotionPerformance(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id for one promotion. Omit it for a ranking.")] string promotion_id = null,
        [Description("Store id for the ranking. Omit it for every store the user manages.")] string store_id = null,
        [Description("Only the last N days. Omit it for the whole run of each promotion.")] int? last_days = null)
    {
        var now = store.Now;

        if (!string.IsNullOrEmpty(promotion_id))
        {
            var promotion = access.GetReadablePromotion(context.User, promotion_id);
            var performance = analytics.GetPerformance(promotion, last_days);

            return new
            {
                promotion = MarketingFormat.Row(promotion, now),
                results = ToResult(performance),
                last_14_days = store.GetDailyStats(promotion)
                    .TakeLast(14)
                    .Select(x => new { date = MarketingFormat.Date(x.Date), x.Redemptions, x.Revenue, discount_cost = x.DiscountCost }),
                how_to_read = "revenue_per_discount: revenue for each unit of discount given; above 8 is strong, below 4 is weak. " +
                    "order_value_uplift_percent compares orders with the promotion to the store average.",
            };
        }

        var stores = access.ResolveStores(context.User, store_id);

        return new
        {
            ranking = store.GetPromotions()
                .Where(x => stores.Any(s => s.Id == x.StoreId) && x.GetStatus(now) is PromotionStatus.Active or PromotionStatus.Paused or PromotionStatus.Expired)
                .Select(x => (Promotion: x, Performance: analytics.GetPerformance(x, last_days)))
                .Where(x => x.Performance.Days > 0)
                .OrderByDescending(x => x.Performance.RevenuePerDiscountUnit ?? decimal.MaxValue)
                .Select(x => new
                {
                    promotion_id = x.Promotion.Id,
                    name = x.Promotion.Name,
                    store_id = x.Promotion.StoreId,
                    status = x.Promotion.GetStatus(now),
                    redemptions = x.Performance.Redemptions,
                    revenue = x.Performance.Revenue,
                    discount_cost = x.Performance.DiscountCost,
                    revenue_per_discount = x.Performance.RevenuePerDiscountUnit,
                    verdict = x.Performance.Verdict,
                }),
            how_to_read = "Sorted by revenue per unit of discount: the best use of the discount budget first.",
        };
    }

    [McpServerTool(Name = "find_promotion_issues", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Health check of promotions with a suggested action for each finding: budget almost spent, discount above the policy " +
        "limit, a strong promotion about to end, an underperforming one, a coupon promotion without coupons, active coupons of " +
        "ended promotions, missing translations for a store language, promotions paused for long. Sorted by severity.")]
    public object FindPromotionIssues(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null,
        [Description("Only this severity: high, medium or low.")] string severity = null)
    {
        var stores = access.ResolveStores(context.User, store_id);
        var issues = analytics.FindIssues(store.GetPromotions().Where(x => stores.Any(s => s.Id == x.StoreId)))
            .Where(x => string.IsNullOrEmpty(severity) || string.Equals(x.Severity, severity, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            total_count = issues.Count,
            issues = issues.Select(x => new
            {
                severity = x.Severity,
                code = x.Code,
                promotion_id = x.PromotionId,
                promotion = x.PromotionName,
                store_id = x.StoreId,
                message = x.Message,
                suggested_action = x.SuggestedAction,
            }),
        };
    }

    [McpServerTool(Name = "find_promotion_conflicts", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Pairs of live, scheduled or draft promotions that run at the same time for the same customers and interfere: " +
        "discounts that stack, an exclusive promotion that hides another, two exclusive ones with the same priority, " +
        "redundant free shipping. Check it before scheduling a new promotion.")]
    public object FindPromotionConflicts(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null,
        [Description("Only the conflicts of this promotion, for example a draft before it is scheduled.")] string promotion_id = null)
    {
        var stores = access.ResolveStores(context.User, store_id);
        var conflicts = analytics.FindConflicts(store.GetPromotions().Where(x => stores.Any(s => s.Id == x.StoreId)));
        if (!string.IsNullOrEmpty(promotion_id))
        {
            var promotion = access.GetReadablePromotion(context.User, promotion_id);
            conflicts = store.GetPromotions()
                .Select(other => analytics.GetConflict(promotion, other))
                .Where(x => x != null)
                .ToList();
        }

        return new
        {
            total_count = conflicts.Count,
            conflicts = conflicts.Select(x => new
            {
                kind = x.Kind,
                store_id = x.StoreId,
                promotion_ids = x.PromotionIds,
                promotions = x.PromotionNames,
                message = x.Message,
                suggested_action = x.SuggestedAction,
            }),
        };
    }

    private static object ToResult(PromotionPerformance performance)
    {
        return new
        {
            days = performance.Days,
            redemptions = performance.Redemptions,
            revenue = performance.Revenue,
            discount_cost = performance.DiscountCost,
            revenue_per_discount = performance.RevenuePerDiscountUnit,
            average_order_value = performance.AverageOrderValue,
            order_value_uplift_percent = performance.AverageOrderValueUplift != null ? MarketingFormat.Percent(performance.AverageOrderValueUplift) : (decimal?)null,
            budget_used_percent = performance.BudgetUsedShare != null ? MarketingFormat.Percent(performance.BudgetUsedShare) : (decimal?)null,
            trend_last_7_days_percent = performance.TrendLast7Days != null ? MarketingFormat.Percent(performance.TrendLast7Days) : (decimal?)null,
            verdict = performance.Verdict,
        };
    }
}
