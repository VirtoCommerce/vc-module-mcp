using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// Layer 2, finders: scoped searches that return ids and the fields that matter for the decision, with a page size
// from IMcpPageSizeResolver so one call cannot flood the agent's context.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[McpPermission(MarketingSampleConstants.ReadPermission)]
public class PromotionFinderTools(
    MarketingDemoStore store,
    MarketingAccess access,
    MarketingAnalytics analytics)
{
    [McpServerTool(Name = "search_promotions", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Find promotions by words in the name, store, status (draft, scheduled, active, paused, expired) or campaign. " +
        "Returns compact rows: id, name, store, status, offer, dates, campaign. Call get_promotion for the details of one.")]
    public async Task<object> SearchPromotions(
        RequestContext<CallToolRequestParams> context,
        IMcpPageSizeResolver pageSizeResolver,
        [Description("Words in the name or the description, for example 'laptop' or 'free shipping'.")] string keyword = null,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null,
        [Description("draft, scheduled, active, paused or expired.")] string status = null,
        [Description("Campaign name, for example 'Black Friday 2026'.")] string campaign = null,
        [Description("Number of rows to return. Omit it for the default page size.")] int? take = null)
    {
        var now = store.Now;
        var stores = access.ResolveStores(context.User, store_id);
        var words = keyword?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        var matches = store.GetPromotions()
            .Where(x => stores.Any(s => s.Id == x.StoreId))
            .Where(x => string.IsNullOrEmpty(status) || string.Equals(x.GetStatus(now), status, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrEmpty(campaign) || x.Campaign?.Contains(campaign, StringComparison.OrdinalIgnoreCase) == true)
            .Where(x => words.All(word =>
                x.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                x.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true ||
                x.TargetCategories.Any(category => category.Contains(word, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => StatusOrder(x.GetStatus(now)))
            .ThenBy(x => x.StartDate)
            .ToList();

        var pageSize = await pageSizeResolver.GetPageSizeAsync(take);

        return new
        {
            total_count = matches.Count,
            promotions = matches.Take(pageSize).Select(x => MarketingFormat.Row(x, now)),
        };
    }

    [McpServerTool(Name = "get_promotion", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Details of one promotion: offer and conditions, dates, priority and exclusivity, customer-facing names by language, " +
        "coupons, results so far, policy warnings and conflicts with other promotions.")]
    public object GetPromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id, from search_promotions.")] string promotion_id)
    {
        var now = store.Now;
        var promotion = access.GetReadablePromotion(context.User, promotion_id);
        var coupons = store.GetCoupons(promotion.Id);
        var performance = analytics.GetPerformance(promotion);
        var conflicts = store.GetPromotions()
            .Select(other => analytics.GetConflict(promotion, other))
            .Where(x => x != null)
            .ToList();

        return new
        {
            promotion_id = promotion.Id,
            name = promotion.Name,
            description = promotion.Description,
            store_id = promotion.StoreId,
            status = promotion.GetStatus(now),
            offer = MarketingFormat.Offer(promotion),
            explanation = MarketingFormat.Explain(promotion, store.GetStore(promotion.StoreId)),
            reward_type = promotion.RewardType,
            reward_value = promotion.RewardValue,
            categories = promotion.TargetCategories,
            min_order_amount = promotion.MinOrderAmount,
            customer_segment_id = promotion.CustomerSegmentId,
            coupon_required = promotion.CouponRequired,
            start = MarketingFormat.Date(promotion.StartDate),
            end = MarketingFormat.Date(promotion.EndDate),
            priority = promotion.Priority,
            exclusive = promotion.IsExclusive,
            discount_budget = promotion.DiscountBudget,
            max_redemptions = promotion.MaxRedemptions,
            campaign = promotion.Campaign,
            owner = promotion.Owner,
            pause_reason = promotion.PauseReason,
            names = promotion.LocalizedNames,
            coupons = new
            {
                total = coupons.Count,
                usable = coupons.Count(x => x.IsActive && !x.IsUsedUp),
                redeemed = coupons.Sum(x => x.Uses),
            },
            results = performance.Days == 0 ? null : new
            {
                days = performance.Days,
                redemptions = performance.Redemptions,
                revenue = performance.Revenue,
                discount_cost = performance.DiscountCost,
                budget_used_percent = performance.BudgetUsedShare != null ? MarketingFormat.Percent(performance.BudgetUsedShare) : (decimal?)null,
                verdict = performance.Verdict,
            },
            policy_warnings = analytics.CheckPolicy(promotion),
            conflicts = conflicts.Select(x => new { x.Kind, other_promotion_id = x.PromotionIds.First(id => id != promotion.Id), x.Message }),
            modified = MarketingFormat.Date(promotion.ModifiedDate),
            modified_by = promotion.ModifiedBy,
        };
    }

    [McpServerTool(Name = "search_coupons", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("List the coupon codes of a promotion with their use: code, uses, limit, whether it is active and when it expires.")]
    public async Task<object> SearchCoupons(
        RequestContext<CallToolRequestParams> context,
        IMcpPageSizeResolver pageSizeResolver,
        [Description("Promotion id.")] string promotion_id,
        [Description("Part of the code.")] string code = null,
        [Description("usable (active and not used up), used or inactive. Omit it for all.")] string state = null,
        [Description("Number of rows to return. Omit it for the default page size.")] int? take = null)
    {
        var promotion = access.GetReadablePromotion(context.User, promotion_id);
        var coupons = store.GetCoupons(promotion.Id)
            .Where(x => string.IsNullOrEmpty(code) || x.Code.Contains(code, StringComparison.OrdinalIgnoreCase))
            .Where(x => state?.ToLowerInvariant() switch
            {
                "usable" => x.IsActive && !x.IsUsedUp,
                "used" => x.Uses > 0,
                "inactive" => !x.IsActive,
                _ => true,
            })
            .ToList();

        var pageSize = await pageSizeResolver.GetPageSizeAsync(take);

        return new
        {
            promotion_id = promotion.Id,
            total_count = coupons.Count,
            coupons = coupons.Take(pageSize).Select(x => new
            {
                code = x.Code,
                uses = x.Uses,
                max_uses = x.MaxUses,
                active = x.IsActive,
                expires = MarketingFormat.Date(x.ExpirationDate),
            }),
        };
    }

    [McpServerTool(Name = "list_customer_segments", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Customer segments a promotion can target, with their size: VIP members, new customers, lapsed customers, B2B accounts.")]
    public object ListCustomerSegments(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null)
    {
        var stores = access.ResolveStores(context.User, store_id);

        return new
        {
            segments = store.GetSegments()
                .Where(x => x.StoreIds.Any(id => stores.Any(s => s.Id == id)))
                .Select(x => new { segment_id = x.Id, name = x.Name, size = x.Size, stores = x.StoreIds, description = x.Description }),
        };
    }

    private static int StatusOrder(string status)
    {
        return status switch
        {
            PromotionStatus.Active => 0,
            PromotionStatus.Scheduled => 1,
            PromotionStatus.Draft => 2,
            PromotionStatus.Paused => 3,
            _ => 4,
        };
    }
}
