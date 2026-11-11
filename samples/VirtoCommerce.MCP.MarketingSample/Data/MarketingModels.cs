using System;
using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.MarketingSample.Data;

public class DemoStore
{
    public string Id { get; set; }

    public string Name { get; set; }

    public string Currency { get; set; }

    // retail or b2b: which retail events matter to the store.
    public string Kind { get; set; } = RetailCalendar.Retail;

    public string[] Languages { get; set; } = [];

    // Baseline without promotions, to measure the uplift of a promotion.
    public decimal AverageOrderValue { get; set; }

    public int OrdersPerDay { get; set; }

    // Monthly discount budget of the marketing team.
    public decimal MonthlyDiscountBudget { get; set; }
}

public static class PromotionState
{
    public const string Draft = "draft";
    public const string Enabled = "enabled";
    public const string Paused = "paused";
}

// What the marketer sees: the state combined with the dates.
public static class PromotionStatus
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Expired = "expired";
}

public static class RewardType
{
    public const string PercentOff = "percent_off";
    public const string AmountOff = "amount_off";
    public const string FreeShipping = "free_shipping";
    public const string BuyXGetY = "buy_x_get_y";
}

public static class TargetType
{
    public const string Cart = "cart";
    public const string Category = "category";
    public const string Shipping = "shipping";
}

public class DemoPromotion
{
    public string Id { get; set; }

    public string StoreId { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    // Customer-facing name by language. A store language without a name is a missing translation.
    public Dictionary<string, string> LocalizedNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string State { get; set; } = PromotionState.Draft;

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string RewardType { get; set; }

    // Percent for percent_off, an amount in the store currency for amount_off, the free items for buy_x_get_y.
    public decimal RewardValue { get; set; }

    public string TargetType { get; set; }

    public string[] TargetCategories { get; set; } = [];

    public decimal? MinOrderAmount { get; set; }

    public string CustomerSegmentId { get; set; }

    public bool CouponRequired { get; set; }

    public int Priority { get; set; }

    public bool IsExclusive { get; set; }

    public int? MaxRedemptions { get; set; }

    public decimal? DiscountBudget { get; set; }

    public string Campaign { get; set; }

    public string Owner { get; set; }

    // Set by the director when the discount is above the policy limit.
    public bool PolicyExceptionApproved { get; set; }

    public string PauseReason { get; set; }

    public DateTime? PausedDate { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? ModifiedDate { get; set; }

    public string ModifiedBy { get; set; }

    // Simulation inputs: redemptions per active day and the order value of orders with the promotion.
    public double DailyRedemptions { get; set; }

    public decimal PromotionAverageOrderValue { get; set; }

    public string GetStatus(DateTime now)
    {
        return State switch
        {
            PromotionState.Draft => PromotionStatus.Draft,
            PromotionState.Paused => PromotionStatus.Paused,
            _ when EndDate < now => PromotionStatus.Expired,
            _ when StartDate > now => PromotionStatus.Scheduled,
            _ => PromotionStatus.Active,
        };
    }

    public bool Overlaps(DateTime? start, DateTime? end)
    {
        return (StartDate ?? DateTime.MinValue) <= (end ?? DateTime.MaxValue) &&
            (start ?? DateTime.MinValue) <= (EndDate ?? DateTime.MaxValue);
    }

    // Creates the promotion with AbstractTypeFactory, so a module that registers a derived type with
    // AbstractTypeFactory<DemoPromotion>.OverrideType gets its type everywhere, the seeded promotions included.
    public static DemoPromotion Create(DemoPromotion template)
    {
        var promotion = AbstractTypeFactory<DemoPromotion>.TryCreateInstance();
        foreach (var property in typeof(DemoPromotion).GetProperties().Where(x => x.CanRead && x.CanWrite))
        {
            property.SetValue(promotion, property.GetValue(template));
        }

        return promotion;
    }

    public virtual DemoPromotion Clone()
    {
        var clone = (DemoPromotion)MemberwiseClone();
        clone.LocalizedNames = new Dictionary<string, string>(LocalizedNames, StringComparer.OrdinalIgnoreCase);
        clone.TargetCategories = [.. TargetCategories];
        return clone;
    }
}

public class DemoCoupon
{
    public string Code { get; set; }

    public string PromotionId { get; set; }

    // Null for a coupon without a limit, such as a public code printed in a newsletter.
    public int? MaxUses { get; set; }

    public int Uses { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; }

    public bool IsUsedUp => MaxUses != null && Uses >= MaxUses;

    public DemoCoupon Clone()
    {
        return (DemoCoupon)MemberwiseClone();
    }
}

public class DemoSegment
{
    public string Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public int Size { get; set; }

    public string[] StoreIds { get; set; } = [];
}

public class DemoDailyStats
{
    public DateTime Date { get; set; }

    public int Redemptions { get; set; }

    public decimal Revenue { get; set; }

    public decimal DiscountCost { get; set; }
}
