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
    private const string NewCustomersSegmentId = "new-customers";

    // Buy X get Y: with at least two items of the category, the cheapest one is at half price.
    private const int BuyXGetYMinItems = 2;
    private const decimal BuyXGetYDiscountShare = 0.5m;

    // ignoreSchedule: evaluate as if the promotion were live, to simulate a draft or a scheduled promotion.
    // assumeCoupon: assume the customer entered a valid code, to simulate a coupon promotion before codes exist.
    public virtual PromotionEvaluation Evaluate(DemoPromotion promotion, DemoOrder order, bool ignoreSchedule = false, bool assumeCoupon = false)
    {
        PromotionEvaluation No(string reason) => new(promotion, false, 0m, reason);

        // The conditions are checked in order and the first one that fails is the reason.
        var rejection = GetRejections(promotion, order, ignoreSchedule, assumeCoupon).FirstOrDefault(x => x != null);
        if (rejection != null)
        {
            return No(rejection);
        }

        var eligible = GetEligibleLines(promotion, order);
        if (eligible.Count == 0)
        {
            return No($"The order has no items in {string.Join(", ", promotion.TargetCategories)}.");
        }

        var discount = GetDiscount(promotion, eligible);
        if (discount < 0)
        {
            return No("The order has one item of the category; the offer needs at least two.");
        }

        return new PromotionEvaluation(promotion, true, Math.Round(discount, MarketingFormat.MoneyDecimals), "All conditions are met.");
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
                Outcome = GetOutcome(x, exclusiveWinner),
            })
            .OrderByDescending(x => x.Applied)
            .ThenByDescending(x => x.Qualifies)
            .ThenBy(x => x.Promotion.Id)
            .ToList();
    }

    // Lazy, so a condition is only checked when the ones before it passed.
    private IEnumerable<string> GetRejections(DemoPromotion promotion, DemoOrder order, bool ignoreSchedule, bool assumeCoupon)
    {
        yield return promotion.StoreId != order.StoreId ? $"The promotion is for store {promotion.StoreId}." : null;

        if (!ignoreSchedule)
        {
            yield return CheckSchedule(promotion, order);
        }

        yield return CheckSegment(promotion, order);

        if (!assumeCoupon)
        {
            yield return CheckCoupon(promotion, order);
        }

        yield return promotion.MinOrderAmount > order.Subtotal
            ? $"The order subtotal {order.Subtotal:N2} is below the minimum of {promotion.MinOrderAmount:N2}."
            : null;
    }

    private static string CheckSchedule(DemoPromotion promotion, DemoOrder order)
    {
        if (promotion.State == PromotionState.Draft)
        {
            return "The promotion is a draft.";
        }

        if (promotion.State == PromotionState.Paused && promotion.PausedDate <= order.Date)
        {
            return $"The promotion was paused on {promotion.PausedDate:yyyy-MM-dd}.";
        }

        if (promotion.StartDate > order.Date)
        {
            return $"The promotion starts {promotion.StartDate:yyyy-MM-dd}, after the order.";
        }

        if (promotion.EndDate < order.Date)
        {
            return $"The promotion ended {promotion.EndDate:yyyy-MM-dd}, before the order.";
        }

        return null;
    }

    private static string CheckSegment(DemoPromotion promotion, DemoOrder order)
    {
        return promotion.CustomerSegmentId switch
        {
            null => null,
            NewCustomersSegmentId when !order.IsFirstOrder => "The promotion is for new customers only, and the customer has ordered before.",
            NewCustomersSegmentId => null,
            _ when !order.CustomerSegments.Contains(promotion.CustomerSegmentId) => $"The customer is not in segment {promotion.CustomerSegmentId}.",
            _ => null,
        };
    }

    private string CheckCoupon(DemoPromotion promotion, DemoOrder order)
    {
        if (!promotion.CouponRequired)
        {
            return null;
        }

        if (string.IsNullOrEmpty(order.CouponCode))
        {
            return "The promotion needs a coupon code, and the order has none.";
        }

        var coupon = store.GetCoupons(promotion.Id).FirstOrDefault(x => string.Equals(x.Code, order.CouponCode, StringComparison.OrdinalIgnoreCase));
        if (coupon == null)
        {
            return $"Code {order.CouponCode} is not a coupon of this promotion.";
        }

        if (!coupon.IsActive || coupon.ExpirationDate < order.Date)
        {
            return $"Code {order.CouponCode} was not active on the order date.";
        }

        return null;
    }

    private static List<DemoOrderLine> GetEligibleLines(DemoPromotion promotion, DemoOrder order)
    {
        return promotion.TargetType == TargetType.Category
            ? order.Lines.Where(x => promotion.TargetCategories.Contains(x.Category, StringComparer.OrdinalIgnoreCase)).ToList()
            : order.Lines;
    }

    // A negative discount means the order does not have enough items for the offer.
    private static decimal GetDiscount(DemoPromotion promotion, List<DemoOrderLine> eligible)
    {
        var eligibleAmount = eligible.Sum(x => x.Amount);

        return promotion.RewardType switch
        {
            RewardType.PercentOff => eligibleAmount * promotion.RewardValue / MarketingFormat.PercentScale,
            RewardType.AmountOff => Math.Min(promotion.RewardValue, eligibleAmount),
            RewardType.FreeShipping => ShippingCost,
            RewardType.BuyXGetY when eligible.Count >= BuyXGetYMinItems => eligible.Min(x => x.Amount) * BuyXGetYDiscountShare,
            RewardType.BuyXGetY => -1m,
            _ => 0m,
        };
    }

    private static string GetOutcome(PromotionEvaluation evaluation, PromotionEvaluation exclusiveWinner)
    {
        if (!evaluation.Qualifies)
        {
            return "not applied";
        }

        return exclusiveWinner == null || evaluation == exclusiveWinner
            ? "applied"
            : $"lost to exclusive promotion {exclusiveWinner.Promotion.Id}";
    }
}
