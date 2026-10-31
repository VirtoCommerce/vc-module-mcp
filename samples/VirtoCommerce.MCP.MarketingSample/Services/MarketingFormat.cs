using System;
using System.Collections.Generic;
using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingSample.Services;

// Compact projections: the 5-8 fields a marketer decides on, never the full object.
public static class MarketingFormat
{
    public const decimal PercentScale = 100m;
    public const int MoneyDecimals = 2;

    private const int PercentDecimals = 1;

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
        var reward = Reward(promotion, Target(promotion));
        var conditions = Conditions(promotion);

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
        return Math.Round((share ?? 0m) * PercentScale, PercentDecimals);
    }

    // A date without a time means the whole day: an end date runs until the end of that day.
    public static DateTime? EndOfDay(DateTime? value)
    {
        if (value == null)
        {
            return null;
        }

        var end = value.Value.TimeOfDay == TimeSpan.Zero ? value.Value.Date.AddDays(1).AddTicks(-1) : value.Value;

        return DateTime.SpecifyKind(end, DateTimeKind.Utc);
    }

    public static DateTime? StartOfDay(DateTime? value)
    {
        return value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private static string Target(DemoPromotion promotion)
    {
        return promotion.TargetType switch
        {
            TargetType.Category => string.Join(", ", promotion.TargetCategories),
            TargetType.Shipping => "shipping",
            _ => "the whole order",
        };
    }

    private static string Reward(DemoPromotion promotion, string target)
    {
        return promotion.RewardType switch
        {
            RewardType.PercentOff => $"{promotion.RewardValue:0.##}% off {target}",
            RewardType.AmountOff => $"{promotion.RewardValue:0.##} off {target}",
            RewardType.FreeShipping => "free shipping",
            RewardType.BuyXGetY => $"buy-X-get-{promotion.RewardValue:0} free on {target}",
            _ => promotion.RewardType,
        };
    }

    private static List<string> Conditions(DemoPromotion promotion)
    {
        var conditions = new List<string>();
        if (promotion.MinOrderAmount != null)
        {
            conditions.Add($"orders over {promotion.MinOrderAmount:0.##}");
        }

        if (promotion.CustomerSegmentId != null)
        {
            conditions.Add($"segment {promotion.CustomerSegmentId}");
        }

        if (promotion.CouponRequired)
        {
            conditions.Add("with a coupon code");
        }

        return conditions;
    }
}
