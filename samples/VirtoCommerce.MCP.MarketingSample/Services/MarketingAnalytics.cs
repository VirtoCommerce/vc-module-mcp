using System;
using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingSample.Services;

public record PromotionPerformance(
    int Days,
    int Redemptions,
    decimal Revenue,
    decimal DiscountCost,
    decimal? BudgetUsedShare,
    decimal? RedemptionsUsedShare,
    decimal AverageOrderValue,
    decimal? AverageOrderValueUplift,
    decimal? RevenuePerDiscountUnit,
    decimal? TrendLast7Days,
    string Verdict);

public record PromotionIssue(string Severity, string Code, string PromotionId, string PromotionName, string StoreId, string Message, string SuggestedAction);

public record PromotionConflict(string Kind, string StoreId, string[] PromotionIds, string[] PromotionNames, string Message, string SuggestedAction);

// Answers a model cannot compute cheaply from raw rows: performance against the store baseline, budget burn,
// policy and translation gaps, and overlaps between promotions. The methods are virtual, so a module can add its
// own rules by registering a derived class (see the extension sample).
public class MarketingAnalytics(MarketingDemoStore store)
{
    public const string SeverityHigh = "high";
    public const string SeverityMedium = "medium";
    public const string SeverityLow = "low";

    public virtual PromotionPerformance GetPerformance(DemoPromotion promotion, int? lastDays = null)
    {
        var daily = store.GetDailyStats(promotion);
        if (lastDays > 0)
        {
            daily = daily.Where(x => x.Date >= store.Now.Date.AddDays(-lastDays.Value)).ToList();
        }

        var allDays = store.GetDailyStats(promotion);
        var demoStore = store.GetStore(promotion.StoreId);
        var redemptions = daily.Sum(x => x.Redemptions);
        var revenue = daily.Sum(x => x.Revenue);
        var cost = daily.Sum(x => x.DiscountCost);
        var aov = redemptions > 0 ? Math.Round(revenue / redemptions, 2) : 0m;

        decimal? trend = null;
        if (daily.Count >= 14)
        {
            var last7 = daily.TakeLast(7).Sum(x => x.Redemptions);
            var previous7 = daily.SkipLast(7).TakeLast(7).Sum(x => x.Redemptions);
            trend = previous7 > 0 ? Math.Round((decimal)(last7 - previous7) / previous7, 3) : null;
        }

        var revenuePerDiscount = cost > 0 ? Math.Round(revenue / cost, 1) : (decimal?)null;

        return new PromotionPerformance(
            Days: daily.Count,
            Redemptions: redemptions,
            Revenue: Math.Round(revenue, 2),
            DiscountCost: Math.Round(cost, 2),
            BudgetUsedShare: promotion.DiscountBudget > 0 ? Math.Round(allDays.Sum(x => x.DiscountCost) / promotion.DiscountBudget.Value, 3) : null,
            RedemptionsUsedShare: promotion.MaxRedemptions > 0 ? Math.Round((decimal)allDays.Sum(x => x.Redemptions) / promotion.MaxRedemptions.Value, 3) : null,
            AverageOrderValue: aov,
            AverageOrderValueUplift: demoStore != null && redemptions > 0 ? Math.Round(aov / demoStore.AverageOrderValue - 1, 3) : null,
            RevenuePerDiscountUnit: revenuePerDiscount,
            TrendLast7Days: trend,
            Verdict: GetVerdict(daily, revenuePerDiscount, demoStore));
    }

    // Spend of all promotions of a store from the first day of the month.
    public virtual decimal GetMonthToDateSpend(string storeId)
    {
        var monthStart = new DateTime(store.Now.Year, store.Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return Math.Round(store.GetPromotions()
            .Where(x => x.StoreId == storeId)
            .SelectMany(store.GetDailyStats)
            .Where(x => x.Date >= monthStart)
            .Sum(x => x.DiscountCost), 2);
    }

    public virtual IList<string> CheckPolicy(DemoPromotion promotion)
    {
        var warnings = new List<string>();

        if (promotion.RewardType == RewardType.PercentOff && promotion.RewardValue > MarketingSampleConstants.MaxPercentWithoutSignOff && !promotion.PolicyExceptionApproved)
        {
            warnings.Add($"{promotion.RewardValue}% is above the {MarketingSampleConstants.MaxPercentWithoutSignOff}% limit of the discount policy: it needs a sign-off by the marketing director.");
        }

        if (promotion.IsExclusive && promotion.Priority < 50)
        {
            warnings.Add("An exclusive promotion should have priority 50 or higher, otherwise a regular promotion may win over it.");
        }

        if (promotion.State != PromotionState.Draft && promotion.EndDate == null && promotion.RewardType == RewardType.PercentOff && promotion.CustomerSegmentId == null)
        {
            warnings.Add("A percent discount for everyone without an end date: the policy asks for an end date.");
        }

        if (promotion.CouponRequired && !store.GetCoupons(promotion.Id).Any(x => x.IsActive && !x.IsUsedUp))
        {
            warnings.Add("The promotion needs a coupon code, but it has no usable coupons: generate coupons before it starts.");
        }

        var demoStore = store.GetStore(promotion.StoreId);
        var missing = demoStore?.Languages.Where(x => !promotion.LocalizedNames.ContainsKey(x)).ToList() ?? [];
        if (missing.Count > 0)
        {
            warnings.Add($"No customer-facing name for {string.Join(", ", missing)}: the storefront shows the internal name there.");
        }

        return warnings;
    }

    public virtual IList<PromotionIssue> FindIssues(IEnumerable<DemoPromotion> promotions)
    {
        var now = store.Now;
        var issues = new List<PromotionIssue>();

        foreach (var promotion in promotions)
        {
            var status = promotion.GetStatus(now);
            var performance = status is PromotionStatus.Active or PromotionStatus.Paused or PromotionStatus.Expired ? GetPerformance(promotion) : null;

            void Add(string severity, string code, string message, string action) =>
                issues.Add(new PromotionIssue(severity, code, promotion.Id, promotion.Name, promotion.StoreId, message, action));

            if (status == PromotionStatus.Active && performance?.BudgetUsedShare >= MarketingSampleConstants.BudgetWarningShare)
            {
                Add(SeverityHigh, "budget_almost_spent", $"{performance.BudgetUsedShare:P0} of the {promotion.DiscountBudget:N0} discount budget is spent, with {DaysLeft(promotion, now)} left.",
                    "Pause it, lower the discount, or raise the budget.");
            }

            if (status == PromotionStatus.Active && performance?.RedemptionsUsedShare >= 0.9m)
            {
                Add(SeverityMedium, "redemption_limit_near", $"{performance.RedemptionsUsedShare:P0} of the redemption limit is used.", "Raise the limit or let it run out.");
            }

            if (status is PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Draft &&
                promotion.RewardType == RewardType.PercentOff && promotion.RewardValue > MarketingSampleConstants.MaxPercentWithoutSignOff && !promotion.PolicyExceptionApproved)
            {
                Add(status == PromotionStatus.Active ? SeverityHigh : SeverityMedium, "policy_limit_exceeded",
                    $"{promotion.RewardValue}% off is above the {MarketingSampleConstants.MaxPercentWithoutSignOff}% policy limit and has no director sign-off.",
                    $"Lower the discount to {MarketingSampleConstants.MaxPercentWithoutSignOff}% or get a sign-off.");
            }

            if (status == PromotionStatus.Active && promotion.EndDate != null && promotion.EndDate.Value - now < TimeSpan.FromDays(3) && performance?.Verdict == "strong")
            {
                Add(SeverityMedium, "ending_while_strong", $"Ends {promotion.EndDate:yyyy-MM-dd} and performs strongly ({performance.RevenuePerDiscountUnit} of revenue per unit of discount).",
                    "Consider extending it with extend_promotion.");
            }

            if (status == PromotionStatus.Active && performance?.Verdict == "weak")
            {
                Add(SeverityMedium, "underperforming", $"{performance.Redemptions} redemptions in {performance.Days} days, below the expected volume.",
                    "Pause it, or change the offer or the audience.");
            }

            if (status is PromotionStatus.Scheduled or PromotionStatus.Draft && promotion.CouponRequired &&
                !store.GetCoupons(promotion.Id).Any(x => x.IsActive && !x.IsUsedUp))
            {
                Add(promotion.StartDate - now < TimeSpan.FromDays(30) ? SeverityHigh : SeverityMedium, "no_coupons",
                    "Needs a coupon code, but has no usable coupons.", "Generate coupons with generate_coupons.");
            }

            if (status == PromotionStatus.Expired)
            {
                var activeCoupons = store.GetCoupons(promotion.Id).Count(x => x.IsActive && !x.IsUsedUp);
                if (activeCoupons > 0)
                {
                    Add(SeverityLow, "stale_coupons", $"Ended {promotion.EndDate:yyyy-MM-dd}, but {activeCoupons} of its coupons are still active.",
                        "Deactivate them with deactivate_stale_coupons.");
                }
            }

            if (status is PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Draft)
            {
                var missing = store.GetStore(promotion.StoreId)?.Languages.Where(x => !promotion.LocalizedNames.ContainsKey(x)).ToList() ?? [];
                if (missing.Count > 0)
                {
                    Add(status == PromotionStatus.Draft ? SeverityLow : SeverityMedium, "missing_translation",
                        $"No customer-facing name for {string.Join(", ", missing)}.", "Add the translation before the promotion goes live.");
                }
            }

            if (status == PromotionStatus.Paused && promotion.PausedDate != null && now - promotion.PausedDate.Value > TimeSpan.FromDays(3))
            {
                Add(SeverityLow, "paused_for_long", $"Paused for {(now - promotion.PausedDate.Value).Days} days: {promotion.PauseReason}.",
                    "Activate it again or end it.");
            }
        }

        return issues
            .OrderBy(x => x.Severity switch { SeverityHigh => 0, SeverityMedium => 1, _ => 2 })
            .ThenBy(x => x.StoreId)
            .ToList();
    }

    public virtual IList<PromotionConflict> FindConflicts(IEnumerable<DemoPromotion> promotions)
    {
        var now = store.Now;
        var live = promotions
            .Where(x => x.GetStatus(now) is PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Draft)
            .OrderBy(x => x.Id)
            .ToList();
        var conflicts = new List<PromotionConflict>();

        for (var i = 0; i < live.Count; i++)
        {
            for (var j = i + 1; j < live.Count; j++)
            {
                var conflict = GetConflict(live[i], live[j]);
                if (conflict != null)
                {
                    conflicts.Add(conflict);
                }
            }
        }

        return conflicts;
    }

    public virtual PromotionConflict GetConflict(DemoPromotion a, DemoPromotion b)
    {
        if (a.StoreId != b.StoreId || a.Id == b.Id || !a.Overlaps(b.StartDate, b.EndDate) || !AudiencesOverlap(a, b))
        {
            return null;
        }

        string[] ids = [a.Id, b.Id];
        string[] names = [a.Name, b.Name];

        if (a.RewardType == RewardType.FreeShipping && b.RewardType == RewardType.FreeShipping)
        {
            return new PromotionConflict("redundant", a.StoreId, ids, names,
                "Both give free shipping to the same customers: one of them never adds anything.",
                "Keep one, or limit the other to customers the first does not cover.");
        }

        if (a.IsExclusive && b.IsExclusive && a.Priority == b.Priority)
        {
            return new PromotionConflict("ambiguous_priority", a.StoreId, ids, names,
                $"Both are exclusive with the same priority {a.Priority}: which one a customer gets is not defined.",
                "Give one of them a higher priority.");
        }

        if (!TargetsOverlap(a, b))
        {
            return null;
        }

        if (a.IsExclusive || b.IsExclusive)
        {
            var (exclusive, other) = a.IsExclusive ? (a, b) : (b, a);
            if (exclusive.Priority >= other.Priority)
            {
                return new PromotionConflict("shadowed", a.StoreId, ids, names,
                    $"'{exclusive.Name}' is exclusive and wins, so '{other.Name}' is not applied where both qualify.",
                    "Check that this is intended; otherwise make one of them non-exclusive or narrow its targets.");
            }

            return null;
        }

        if (a.RewardType == RewardType.PercentOff && b.RewardType == RewardType.PercentOff)
        {
            return new PromotionConflict("stacking", a.StoreId, ids, names,
                $"Both apply to the same items and stack: up to {a.RewardValue + b.RewardValue}% off in total.",
                $"Make one exclusive, or check that the combined discount stays within the {MarketingSampleConstants.MaxPercentWithoutSignOff}% policy limit.");
        }

        return null;
    }

    private static bool AudiencesOverlap(DemoPromotion a, DemoPromotion b)
    {
        return a.CustomerSegmentId == null || b.CustomerSegmentId == null || a.CustomerSegmentId == b.CustomerSegmentId;
    }

    private static bool TargetsOverlap(DemoPromotion a, DemoPromotion b)
    {
        if (a.TargetType == TargetType.Shipping || b.TargetType == TargetType.Shipping)
        {
            return false;
        }

        return a.TargetType == TargetType.Cart || b.TargetType == TargetType.Cart ||
            a.TargetCategories.Intersect(b.TargetCategories, StringComparer.OrdinalIgnoreCase).Any();
    }

    // Weak when it ran two weeks and reached less than 1% of the store's daily orders, or gives too little back per unit of discount.
    private static string GetVerdict(IReadOnlyCollection<DemoDailyStats> daily, decimal? revenuePerDiscount, DemoStore store)
    {
        if (daily.Count == 0)
        {
            return "no_data";
        }

        var redemptionsPerDay = (double)daily.Sum(x => x.Redemptions) / daily.Count;
        var expectedPerDay = Math.Max(0.5, (store?.OrdersPerDay ?? 100) * 0.01);
        if (daily.Count >= 14 && redemptionsPerDay < expectedPerDay)
        {
            return "weak";
        }

        return revenuePerDiscount switch
        {
            >= 8 => "strong",
            >= 4 => "ok",
            null => "ok",
            _ => "weak",
        };
    }

    private static string DaysLeft(DemoPromotion promotion, DateTime now)
    {
        return promotion.EndDate == null ? "no end date" : $"{Math.Max(0, (int)Math.Ceiling((promotion.EndDate.Value - now).TotalDays))} days";
    }
}
