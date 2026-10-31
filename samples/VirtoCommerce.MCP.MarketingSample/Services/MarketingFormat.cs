using System;
using System.Linq;
using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingSample.Services;

// Compact projections: the 5-8 fields a marketer decides on, never the full object.
public static class MarketingFormat
{
    public static object Row(DemoPromotion promotion, DateTime now)
    {
        return new
        {
            promotion_id = promotion.Id,
            name = promotion.Name,
            store_id = promotion.StoreId,
            status = promotion.GetStatus(now),
            offer = Offer(promotion),
            start = Date(promotion.StartDate),
            end = Date(promotion.EndDate),
            campaign = promotion.Campaign,
        };
    }

    public static string Offer(DemoPromotion promotion)
    {
        var target = promotion.TargetType switch
        {
            TargetType.Category => string.Join(", ", promotion.TargetCategories),
            TargetType.Shipping => "shipping",
            _ => "the whole order",
        };

        var reward = promotion.RewardType switch
        {
            RewardType.PercentOff => $"{promotion.RewardValue:0.##}% off {target}",
            RewardType.AmountOff => $"{promotion.RewardValue:0.##} off {target}",
            RewardType.FreeShipping => "free shipping",
            RewardType.BuyXGetY => $"buy-X-get-{promotion.RewardValue:0} free on {target}",
            _ => promotion.RewardType,
        };

        var conditions = new[]
        {
            promotion.MinOrderAmount != null ? $"orders over {promotion.MinOrderAmount:0.##}" : null,
            promotion.CustomerSegmentId != null ? $"segment {promotion.CustomerSegmentId}" : null,
            promotion.CouponRequired ? "with a coupon code" : null,
        }.Where(x => x != null).ToList();

        return conditions.Count == 0 ? reward : $"{reward} ({string.Join(", ", conditions)})";
    }

    // One paragraph a marketer can confirm: "15% off Laptops in store Electronics (USD), from 2026-10-12 to 2026-10-31, ...".
    public static string Explain(DemoPromotion promotion, DemoStore store)
    {
        var dates = (promotion.StartDate, promotion.EndDate) switch
        {
            (null, null) => "with no dates set",
            (_, null) => $"from {Date(promotion.StartDate)}, with no end date",
            (null, _) => $"until {Date(promotion.EndDate)}",
            _ => $"from {Date(promotion.StartDate)} to {Date(promotion.EndDate)}",
        };

        var combining = promotion.IsExclusive
            ? $"exclusive: when it qualifies, no other promotion applies, unless an exclusive one with a priority above {promotion.Priority} also qualifies"
            : "combines with other promotions";

        return $"{Offer(promotion)} in store {promotion.StoreId} ({store?.Currency}), {dates}; {combining}.";
    }

    public static string Date(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd");
    }

    public static decimal Percent(decimal? share)
    {
        return Math.Round((share ?? 0m) * 100m, 1);
    }

    // A date without a time means the whole day: an end date runs until the end of that day.
    public static DateTime? EndOfDay(DateTime? value)
    {
        return value == null ? null : value.Value.TimeOfDay == TimeSpan.Zero ? DateTime.SpecifyKind(value.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    public static DateTime? StartOfDay(DateTime? value)
    {
        return value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}
