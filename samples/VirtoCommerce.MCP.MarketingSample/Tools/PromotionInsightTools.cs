using System;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// The tools that make the agent an expert on promotions: what a promotion can say, what it would cost on real
// orders, and why an order did or did not get a discount.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[McpPermission(MarketingSampleConstants.ReadPermission)]
public class PromotionInsightTools(
    MarketingDemoStore store,
    MarketingAccess access,
    PromotionEvaluator evaluator)
{
    private const int SampleSize = 3;

    [McpServerTool(Name = "describe_promotion_rules", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("What a promotion can say: the reward types, the targets, the conditions and how promotions combine. " +
        "Read it before creating or changing a promotion, so you only use rules that exist.")]
    public static object DescribePromotionRules()
    {
        return new
        {
            rewards = new object[]
            {
                new { reward_type = RewardType.PercentOff, reward_value = "percent, 1 to 50", meaning = "Percent off the targeted items or the whole order." },
                new { reward_type = RewardType.AmountOff, reward_value = "amount in the store currency", meaning = "Fixed amount off, at most the value of the targeted items." },
                new { reward_type = RewardType.FreeShipping, reward_value = "0", meaning = "Free standard shipping." },
                new { reward_type = RewardType.BuyXGetY, reward_value = "number of free items", meaning = "With two or more items of the category, the cheapest one at half price." },
            },
            targets = new object[]
            {
                new { target_type = TargetType.Category, meaning = "Items of the listed categories (categories)." },
                new { target_type = TargetType.Cart, meaning = "The whole order." },
                new { target_type = TargetType.Shipping, meaning = "The shipping cost; use with free_shipping." },
            },
            conditions = new object[]
            {
                new { name = "start_date / end_date", meaning = "The days the promotion runs; an end date is the last full day." },
                new { name = "min_order_amount", meaning = "The order subtotal must reach this amount." },
                new { name = "customer_segment_id", meaning = "Only customers of the segment (list_customer_segments). new-customers means a first order." },
                new { name = "coupon_required", meaning = "The customer must enter a code of the promotion (generate_coupons)." },
            },
            combining = new
            {
                rule = "Promotions that qualify stack, except exclusive ones: when exclusive promotions qualify, the one with the highest priority wins and no other promotion applies.",
                exclusive_priority = "Exclusive promotions have priority 50 or higher.",
            },
            policy = $"At most {MarketingSampleConstants.MaxPercentWithoutSignOff}% off without a director sign-off; read marketing://policies/discounts.",
        };
    }

    [McpServerTool(Name = "simulate_promotion", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Run a promotion, even a draft or a scheduled one, against the store's real orders of the last days and " +
        "return what it would have done: how many orders qualify, the average discount, the total discount exposure, " +
        "the orders with the biggest discount, and the most common reasons orders miss it. Use it before activating a promotion.")]
    public object SimulatePromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("Number of past days of orders, 1 to 30. Defaults to 30.")] int last_days = 30)
    {
        var promotion = access.GetReadablePromotion(context.User, promotion_id);
        last_days = Math.Clamp(last_days, 1, 30);

        var orders = store.GetOrders(promotion.StoreId, store.Now.Date.AddDays(-last_days));
        var results = orders.Select(order => (Order: order, Evaluation: evaluator.Evaluate(promotion, order, ignoreSchedule: true, assumeCoupon: true))).ToList();
        var hits = results.Where(x => x.Evaluation.Qualifies).ToList();
        var exposure = hits.Sum(x => x.Evaluation.Discount);
        var currency = store.GetStore(promotion.StoreId)?.Currency;

        return new
        {
            promotion = MarketingFormat.Row(promotion, store.Now),
            assumptions = promotion.CouponRequired
                ? "Every order is assumed to use a code, so this is the upper bound of a coupon promotion."
                : "The promotion is evaluated as if it had been live on every day.",
            orders_evaluated = orders.Count,
            qualifying_orders = hits.Count,
            hit_rate_percent = orders.Count > 0 ? Math.Round(100m * hits.Count / orders.Count, 1) : 0,
            average_discount = hits.Count > 0 ? Math.Round(exposure / hits.Count, 2) : 0,
            total_discount = Math.Round(exposure, 2),
            discount_per_30_days = Math.Round(exposure / last_days * 30, 0),
            currency,
            biggest_discounts = hits
                .OrderByDescending(x => x.Evaluation.Discount)
                .Take(SampleSize)
                .Select(x => new { order = x.Order.Number, subtotal = x.Order.Subtotal, discount = x.Evaluation.Discount }),
            why_orders_miss = results
                .Where(x => !x.Evaluation.Qualifies)
                .GroupBy(x => ReasonKind(x.Evaluation.Reason))
                .OrderByDescending(x => x.Count())
                .Take(SampleSize)
                .Select(x => new { reason = x.Key, orders = x.Count(), example = x.First().Evaluation.Reason }),
        };
    }

    [McpServerTool(Name = "explain_promotion_evaluation", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Why an order got, or did not get, each promotion of its store: the coupon, the customer segment, the dates, " +
        "the minimum amount, the categories, or an exclusive promotion that won. Use it for complaints such as " +
        "'my code did not work on order CO-10482'.")]
    public object ExplainPromotionEvaluation(
        RequestContext<CallToolRequestParams> context,
        [Description("Order number, for example CO-10482.")] string order_number)
    {
        var order = store.GetOrder(order_number) ?? throw new McpException($"Order '{order_number}' was not found.");
        access.GetReadableStore(context.User, order.StoreId);

        var evaluations = evaluator.Explain(order);

        return new
        {
            order = new
            {
                number = order.Number,
                store_id = order.StoreId,
                date = order.Date.ToString("yyyy-MM-dd HH:mm"),
                customer = order.CustomerId,
                first_order = order.IsFirstOrder,
                segments = order.CustomerSegments,
                coupon_code = order.CouponCode,
                subtotal = order.Subtotal,
                items = order.Lines.Select(x => $"{x.Product} ({x.Category}) {x.Amount:N2}"),
            },
            applied = evaluations.Where(x => x.Applied).Select(x => new { promotion_id = x.Promotion.Id, name = x.Promotion.Name, discount = x.Discount }),
            not_applied = evaluations
                .Where(x => !x.Applied && x.Promotion.GetStatus(order.Date) is not (PromotionStatus.Draft))
                .Select(x => new { promotion_id = x.Promotion.Id, name = x.Promotion.Name, outcome = x.Outcome, reason = x.Reason }),
        };
    }

    [McpServerTool(Name = "get_coupon_usage", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("How the codes of a promotion are used: unused, used, used up and deactivated codes, and redemptions by day " +
        "over the last 30 days.")]
    public object GetCouponUsage(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id)
    {
        var promotion = access.GetReadablePromotion(context.User, promotion_id);
        var coupons = store.GetCoupons(promotion.Id);
        var codes = coupons.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var redemptions = store.GetOrders(promotion.StoreId, store.Now.Date.AddDays(-30))
            .Where(x => x.CouponCode != null && codes.Contains(x.CouponCode))
            .Where(x => evaluator.Evaluate(promotion, x).Qualifies)
            .ToList();

        return new
        {
            promotion_id = promotion.Id,
            codes = new
            {
                total = coupons.Count,
                unused = coupons.Count(x => x.IsActive && x.Uses == 0),
                used = coupons.Count(x => x.Uses > 0 && !x.IsUsedUp),
                used_up = coupons.Count(x => x.IsUsedUp),
                deactivated = coupons.Count(x => !x.IsActive),
                total_redemptions = coupons.Sum(x => x.Uses),
            },
            redemptions_last_30_days = redemptions
                .GroupBy(x => x.Date.Date)
                .OrderBy(x => x.Key)
                .Select(x => new { date = MarketingFormat.Date(x.Key), redemptions = x.Count() }),
            rejected_code_attempts_last_30_days = store.GetOrders(promotion.StoreId, store.Now.Date.AddDays(-30))
                .Count(x => x.CouponCode != null && codes.Contains(x.CouponCode) && !evaluator.Evaluate(promotion, x).Qualifies),
        };
    }

    private static string ReasonKind(string reason)
    {
        return reason switch
        {
            _ when reason.Contains("segment", StringComparison.OrdinalIgnoreCase) || reason.Contains("new customers", StringComparison.OrdinalIgnoreCase) => "customer not in the audience",
            _ when reason.Contains("minimum", StringComparison.OrdinalIgnoreCase) => "below the minimum amount",
            _ when reason.Contains("no items", StringComparison.OrdinalIgnoreCase) || reason.Contains("one item", StringComparison.OrdinalIgnoreCase) => "no qualifying items",
            _ => "other",
        };
    }
}
