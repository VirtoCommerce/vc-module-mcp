using System;
using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingSample.Services;

public record PromotionEvaluation(DemoPromotion Promotion, bool Qualifies, decimal Discount, string Reason)
{
    public bool Applied { get; init; }

    public string Outcome { get; init; }
}

// Evaluates promotions against an order the way the Marketing module's promotion evaluator does for a cart: the store,
// the dates, the customer segment, the coupon, the minimum amount and the targeted categories, then exclusivity.
// A real module would call IMarketingPromoEvaluator with a PromotionEvaluationContext built from the order.
public class PromotionEvaluator(MarketingDemoStore store)
{
    private const decimal ShippingCost = 8.95m;

    // ignoreSchedule: evaluate as if the promotion were live, to simulate a draft or a scheduled promotion.
    // assumeCoupon: assume the customer entered a valid code, to simulate a coupon promotion before codes exist.
    public virtual PromotionEvaluation Evaluate(DemoPromotion promotion, DemoOrder order, bool ignoreSchedule = false, bool assumeCoupon = false)
    {
        PromotionEvaluation No(string reason) => new(promotion, false, 0m, reason);

        if (promotion.StoreId != order.StoreId)
        {
            return No($"The promotion is for store {promotion.StoreId}.");
        }

        if (!ignoreSchedule)
        {
            if (promotion.State == PromotionState.Draft)
            {
                return No("The promotion is a draft.");
            }

            if (promotion.State == PromotionState.Paused && promotion.PausedDate <= order.Date)
            {
                return No($"The promotion was paused on {promotion.PausedDate:yyyy-MM-dd}.");
            }

            if (promotion.StartDate > order.Date)
            {
                return No($"The promotion starts {promotion.StartDate:yyyy-MM-dd}, after the order.");
            }

            if (promotion.EndDate < order.Date)
            {
                return No($"The promotion ended {promotion.EndDate:yyyy-MM-dd}, before the order.");
            }
        }

        switch (promotion.CustomerSegmentId)
        {
            case null:
                break;
            case "new-customers" when !order.IsFirstOrder:
                return No("The promotion is for new customers only, and the customer has ordered before.");
            case "new-customers":
                break;
            default:
                if (!order.CustomerSegments.Contains(promotion.CustomerSegmentId))
                {
                    return No($"The customer is not in segment {promotion.CustomerSegmentId}.");
                }

                break;
        }

        if (promotion.CouponRequired && !assumeCoupon)
        {
            if (string.IsNullOrEmpty(order.CouponCode))
            {
                return No("The promotion needs a coupon code, and the order has none.");
            }

            var coupon = store.GetCoupons(promotion.Id).FirstOrDefault(x => string.Equals(x.Code, order.CouponCode, StringComparison.OrdinalIgnoreCase));
            if (coupon == null)
            {
                return No($"Code {order.CouponCode} is not a coupon of this promotion.");
            }

            if (!coupon.IsActive || coupon.ExpirationDate < order.Date)
            {
                return No($"Code {order.CouponCode} was not active on the order date.");
            }
        }

        if (promotion.MinOrderAmount > order.Subtotal)
        {
            return No($"The order subtotal {order.Subtotal:N2} is below the minimum of {promotion.MinOrderAmount:N2}.");
        }

        var eligible = promotion.TargetType == TargetType.Category
            ? order.Lines.Where(x => promotion.TargetCategories.Contains(x.Category, StringComparer.OrdinalIgnoreCase)).ToList()
            : order.Lines;
        if (eligible.Count == 0)
        {
            return No($"The order has no items in {string.Join(", ", promotion.TargetCategories)}.");
        }

        var eligibleAmount = eligible.Sum(x => x.Amount);
        var discount = promotion.RewardType switch
        {
            RewardType.PercentOff => eligibleAmount * promotion.RewardValue / 100m,
            RewardType.AmountOff => Math.Min(promotion.RewardValue, eligibleAmount),
            RewardType.FreeShipping => ShippingCost,
            RewardType.BuyXGetY when eligible.Count >= 2 => eligible.Min(x => x.Amount) * 0.5m,
            RewardType.BuyXGetY => -1m,
            _ => 0m,
        };

        if (discount < 0)
        {
            return No("The order has one item of the category; the offer needs at least two.");
        }

        return new PromotionEvaluation(promotion, true, Math.Round(discount, 2), "All conditions are met.");
    }

    // Every promotion of the store for one order, with the one that wins when exclusive promotions compete.
    public virtual IList<PromotionEvaluation> Explain(DemoOrder order)
    {
        var evaluations = store.GetPromotions()
            .Where(x => x.StoreId == order.StoreId)
            .Select(x => Evaluate(x, order))
            .ToList();

        var exclusiveWinner = evaluations
            .Where(x => x.Qualifies && x.Promotion.IsExclusive)
            .OrderByDescending(x => x.Promotion.Priority)
            .ThenByDescending(x => x.Discount)
            .FirstOrDefault();

        return evaluations
            .Select(x => x with
            {
                Applied = x.Qualifies && (exclusiveWinner == null || x == exclusiveWinner),
                Outcome = !x.Qualifies
                    ? "not applied"
                    : exclusiveWinner == null || x == exclusiveWinner
                        ? "applied"
                        : $"lost to exclusive promotion {exclusiveWinner.Promotion.Id}",
            })
            .OrderByDescending(x => x.Applied)
            .ThenByDescending(x => x.Qualifies)
            .ThenBy(x => x.Promotion.Id)
            .ToList();
    }
}
