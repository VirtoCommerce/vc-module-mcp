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

    // Sort rank of a severity other than high and medium: it sorts with the low ones.
    private const int OtherSeverityRank = 2;

    private const string VerdictStrong = "strong";
    private const string VerdictOk = "ok";
    private const string VerdictWeak = "weak";
    private const string VerdictNoData = "no_data";

    // The trend compares the last week with the week before, so it needs two full weeks of results.
    private const int TrendWindowDays = 7;
    private const int TrendMinDays = TrendWindowDays * 2;

    private const int ShareDecimals = 3;
    private const int RatioDecimals = 1;

    private const decimal RedemptionLimitWarningShare = 0.9m;
    private const int EndingSoonDays = 3;
    private const int CouponLeadDays = 30;
    private const int LongPauseDays = 3;

    // Weak when it ran two weeks and reached less than 1% of the store's daily orders; strong and ok by the
    // revenue per unit of discount.
    private const int VerdictMinDays = 14;
    private const double ExpectedReachShare = 0.01;
    private const double MinExpectedRedemptionsPerDay = 0.5;
    private const int DefaultOrdersPerDay = 100;
    private const decimal StrongRevenuePerDiscount = 8m;
    private const decimal OkRevenuePerDiscount = 4m;

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
        var aov = redemptions > 0 ? Math.Round(revenue / redemptions, MarketingFormat.MoneyDecimals) : 0m;

        decimal? trend = null;
        if (daily.Count >= TrendMinDays)
        {
            var last7 = daily.TakeLast(TrendWindowDays).Sum(x => x.Redemptions);
            var previous7 = daily.SkipLast(TrendWindowDays).TakeLast(TrendWindowDays).Sum(x => x.Redemptions);
            trend = previous7 > 0 ? Math.Round((decimal)(last7 - previous7) / previous7, ShareDecimals) : null;
        }

        var revenuePerDiscount = cost > 0 ? Math.Round(revenue / cost, RatioDecimals) : (decimal?)null;

        return new PromotionPerformance(
            Days: daily.Count,
            Redemptions: redemptions,
            Revenue: Math.Round(revenue, MarketingFormat.MoneyDecimals),
            DiscountCost: Math.Round(cost, MarketingFormat.MoneyDecimals),
            BudgetUsedShare: promotion.DiscountBudget > 0 ? Math.Round(allDays.Sum(x => x.DiscountCost) / promotion.DiscountBudget.Value, ShareDecimals) : null,
            RedemptionsUsedShare: promotion.MaxRedemptions > 0 ? Math.Round((decimal)allDays.Sum(x => x.Redemptions) / promotion.MaxRedemptions.Value, ShareDecimals) : null,
            AverageOrderValue: aov,
            AverageOrderValueUplift: demoStore != null && redemptions > 0 ? Math.Round(aov / demoStore.AverageOrderValue - 1, ShareDecimals) : null,
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
            .Sum(x => x.DiscountCost), MarketingFormat.MoneyDecimals);
    }

    // The list stays mutable: a derived class adds its own warnings to it.
    public virtual IList<string> CheckPolicy(DemoPromotion promotion)
    {
        return new[]
            {
                GetPercentLimitWarning(promotion),
                GetExclusivePriorityWarning(promotion),
                GetEndDateWarning(promotion),
                GetCouponWarning(promotion),
                GetTranslationWarning(promotion),
            }
            .Where(x => x != null)
            .ToList();
    }

    public virtual IList<PromotionIssue> FindIssues(IEnumerable<DemoPromotion> promotions)
    {
        var now = store.Now;

        return SortIssues(promotions.SelectMany(promotion => GetIssues(promotion, now)));
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
        if (!CanInterfere(a, b))
        {
            return null;
        }

        string[] ids = [a.Id, b.Id];
        string[] names = [a.Name, b.Name];

        if (BothGiveFreeShipping(a, b))
        {
            return new PromotionConflict("redundant", a.StoreId, ids, names,
                "Both give free shipping to the same customers: one of them never adds anything.",
                "Keep one, or limit the other to customers the first does not cover.");
        }

        if (HaveAmbiguousPriority(a, b))
        {
            return new PromotionConflict("ambiguous_priority", a.StoreId, ids, names,
                $"Both are exclusive with the same priority {a.Priority}: which one a customer gets is not defined.",
                "Give one of them a higher priority.");
        }

        if (!TargetsOverlap(a, b))
        {
            return null;
        }

        return a.IsExclusive || b.IsExclusive
            ? GetShadowedConflict(a, b, ids, names)
            : GetStackingConflict(a, b, ids, names);
    }

    // Severity first, then store: the order of every issue list, including the issues a derived class adds.
    protected static IList<PromotionIssue> SortIssues(IEnumerable<PromotionIssue> issues)
    {
        return issues
            .OrderBy(x => x.Severity switch { SeverityHigh => 0, SeverityMedium => 1, _ => OtherSeverityRank })
            .ThenBy(x => x.StoreId)
            .ToList();
    }

    private static string GetPercentLimitWarning(DemoPromotion promotion)
    {
        return NeedsSignOff(promotion)
            ? $"{promotion.RewardValue}% is above the {MarketingSampleConstants.MaxPercentWithoutSignOff}% limit of the discount policy: it needs a sign-off by the marketing director."
            : null;
    }

    private static string GetExclusivePriorityWarning(DemoPromotion promotion)
    {
        return promotion.IsExclusive && promotion.Priority < MarketingSampleConstants.MinExclusivePriority
            ? $"An exclusive promotion should have priority {MarketingSampleConstants.MinExclusivePriority} or higher, otherwise a regular promotion may win over it."
            : null;
    }

    private static string GetEndDateWarning(DemoPromotion promotion)
    {
        var evergreenForEveryone = promotion.EndDate == null && promotion.CustomerSegmentId == null;

        return promotion.State != PromotionState.Draft && evergreenForEveryone && promotion.RewardType == RewardType.PercentOff
            ? "A percent discount for everyone without an end date: the policy asks for an end date."
            : null;
    }

    private string GetCouponWarning(DemoPromotion promotion)
    {
        return promotion.CouponRequired && !HasUsableCoupons(promotion)
            ? "The promotion needs a coupon code, but it has no usable coupons: generate coupons before it starts."
            : null;
    }

    private string GetTranslationWarning(DemoPromotion promotion)
    {
        var missing = GetMissingLanguages(promotion);

        return missing.Count > 0
            ? $"No customer-facing name for {string.Join(", ", missing)}: the storefront shows the internal name there."
            : null;
    }

    // One check per kind of issue; each returns null when the promotion does not have that issue.
    private IEnumerable<PromotionIssue> GetIssues(DemoPromotion promotion, DateTime now)
    {
        var status = promotion.GetStatus(now);
        var performance = status is PromotionStatus.Active or PromotionStatus.Paused or PromotionStatus.Expired ? GetPerformance(promotion) : null;
        var check = new IssueCheck(promotion, status, performance, now);

        return new[]
            {
                GetBudgetIssue(check),
                GetRedemptionLimitIssue(check),
                GetPolicyLimitIssue(check),
                GetEndingWhileStrongIssue(check),
                GetUnderperformingIssue(check),
                GetNoCouponsIssue(check),
                GetStaleCouponsIssue(check),
                GetMissingTranslationIssue(check),
                GetPausedForLongIssue(check),
            }
            .Where(x => x != null);
    }

    private static PromotionIssue GetBudgetIssue(IssueCheck check)
    {
        if (check.Status == PromotionStatus.Active && check.Performance?.BudgetUsedShare >= MarketingSampleConstants.BudgetWarningShare)
        {
            return check.Issue(SeverityHigh, "budget_almost_spent",
                $"{check.Performance.BudgetUsedShare:P0} of the {check.Promotion.DiscountBudget:N0} discount budget is spent, with {DaysLeft(check.Promotion, check.Now)} left.",
                "Pause it, lower the discount, or raise the budget.");
        }

        return null;
    }

    private static PromotionIssue GetRedemptionLimitIssue(IssueCheck check)
    {
        if (check.Status == PromotionStatus.Active && check.Performance?.RedemptionsUsedShare >= RedemptionLimitWarningShare)
        {
            return check.Issue(SeverityMedium, "redemption_limit_near", $"{check.Performance.RedemptionsUsedShare:P0} of the redemption limit is used.",
                "Raise the limit or let it run out.");
        }

        return null;
    }

    private static PromotionIssue GetPolicyLimitIssue(IssueCheck check)
    {
        if (check.Status is PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Draft && NeedsSignOff(check.Promotion))
        {
            return check.Issue(check.Status == PromotionStatus.Active ? SeverityHigh : SeverityMedium, "policy_limit_exceeded",
                $"{check.Promotion.RewardValue}% off is above the {MarketingSampleConstants.MaxPercentWithoutSignOff}% policy limit and has no director sign-off.",
                $"Lower the discount to {MarketingSampleConstants.MaxPercentWithoutSignOff}% or get a sign-off.");
        }

        return null;
    }

    private static PromotionIssue GetEndingWhileStrongIssue(IssueCheck check)
    {
        var endsSoon = check.Promotion.EndDate != null && check.Promotion.EndDate.Value - check.Now < TimeSpan.FromDays(EndingSoonDays);
        if (check.Status == PromotionStatus.Active && endsSoon && check.Performance?.Verdict == VerdictStrong)
        {
            return check.Issue(SeverityMedium, "ending_while_strong",
                $"Ends {check.Promotion.EndDate:yyyy-MM-dd} and performs strongly ({check.Performance.RevenuePerDiscountUnit} of revenue per unit of discount).",
                "Consider extending it with extend_promotion.");
        }

        return null;
    }

    private static PromotionIssue GetUnderperformingIssue(IssueCheck check)
    {
        if (check.Status == PromotionStatus.Active && check.Performance?.Verdict == VerdictWeak)
        {
            return check.Issue(SeverityMedium, "underperforming", $"{check.Performance.Redemptions} redemptions in {check.Performance.Days} days, below the expected volume.",
                "Pause it, or change the offer or the audience.");
        }

        return null;
    }

    private PromotionIssue GetNoCouponsIssue(IssueCheck check)
    {
        if (check.Status is PromotionStatus.Scheduled or PromotionStatus.Draft && check.Promotion.CouponRequired && !HasUsableCoupons(check.Promotion))
        {
            return check.Issue(check.Promotion.StartDate - check.Now < TimeSpan.FromDays(CouponLeadDays) ? SeverityHigh : SeverityMedium, "no_coupons",
                "Needs a coupon code, but has no usable coupons.", "Generate coupons with generate_coupons.");
        }

        return null;
    }

    private PromotionIssue GetStaleCouponsIssue(IssueCheck check)
    {
        if (check.Status != PromotionStatus.Expired)
        {
            return null;
        }

        var activeCoupons = store.GetCoupons(check.Promotion.Id).Count(x => x.IsActive && !x.IsUsedUp);
        if (activeCoupons > 0)
        {
            return check.Issue(SeverityLow, "stale_coupons", $"Ended {check.Promotion.EndDate:yyyy-MM-dd}, but {activeCoupons} of its coupons are still active.",
                "Deactivate them with deactivate_stale_coupons.");
        }

        return null;
    }

    private PromotionIssue GetMissingTranslationIssue(IssueCheck check)
    {
        if (check.Status is not (PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Draft))
        {
            return null;
        }

        var missing = GetMissingLanguages(check.Promotion);
        if (missing.Count > 0)
        {
            return check.Issue(check.Status == PromotionStatus.Draft ? SeverityLow : SeverityMedium, "missing_translation",
                $"No customer-facing name for {string.Join(", ", missing)}.", "Add the translation before the promotion goes live.");
        }

        return null;
    }

    private static PromotionIssue GetPausedForLongIssue(IssueCheck check)
    {
        var pausedDate = check.Promotion.PausedDate;
        if (check.Status == PromotionStatus.Paused && pausedDate != null && check.Now - pausedDate.Value > TimeSpan.FromDays(LongPauseDays))
        {
            return check.Issue(SeverityLow, "paused_for_long", $"Paused for {(check.Now - pausedDate.Value).Days} days: {check.Promotion.PauseReason}.",
                "Activate it again or end it.");
        }

        return null;
    }

    private static bool NeedsSignOff(DemoPromotion promotion)
    {
        return promotion.RewardType == RewardType.PercentOff && promotion.RewardValue > MarketingSampleConstants.MaxPercentWithoutSignOff && !promotion.PolicyExceptionApproved;
    }

    private bool HasUsableCoupons(DemoPromotion promotion)
    {
        return store.GetCoupons(promotion.Id).Any(x => x.IsActive && !x.IsUsedUp);
    }

    // Languages of the promotion's store without a customer-facing name.
    private List<string> GetMissingLanguages(DemoPromotion promotion)
    {
        return store.GetStore(promotion.StoreId)?.Languages.Where(x => !promotion.LocalizedNames.ContainsKey(x)).ToList() ?? [];
    }

    // Two different promotions of one store whose dates and audiences overlap.
    private static bool CanInterfere(DemoPromotion a, DemoPromotion b)
    {
        return a.StoreId == b.StoreId && a.Id != b.Id && a.Overlaps(b.StartDate, b.EndDate) && AudiencesOverlap(a, b);
    }

    private static bool BothGiveFreeShipping(DemoPromotion a, DemoPromotion b)
    {
        return a.RewardType == RewardType.FreeShipping && b.RewardType == RewardType.FreeShipping;
    }

    private static bool HaveAmbiguousPriority(DemoPromotion a, DemoPromotion b)
    {
        return a.IsExclusive && b.IsExclusive && a.Priority == b.Priority;
    }

    private static PromotionConflict GetShadowedConflict(DemoPromotion a, DemoPromotion b, string[] ids, string[] names)
    {
        var (exclusive, other) = a.IsExclusive ? (a, b) : (b, a);
        if (exclusive.Priority < other.Priority)
        {
            return null;
        }

        return new PromotionConflict("shadowed", a.StoreId, ids, names,
            $"'{exclusive.Name}' is exclusive and wins, so '{other.Name}' is not applied where both qualify.",
            "Check that this is intended; otherwise make one of them non-exclusive or narrow its targets.");
    }

    private static PromotionConflict GetStackingConflict(DemoPromotion a, DemoPromotion b, string[] ids, string[] names)
    {
        if (a.RewardType != RewardType.PercentOff || b.RewardType != RewardType.PercentOff)
        {
            return null;
        }

        return new PromotionConflict("stacking", a.StoreId, ids, names,
            $"Both apply to the same items and stack: up to {a.RewardValue + b.RewardValue}% off in total.",
            $"Make one exclusive, or check that the combined discount stays within the {MarketingSampleConstants.MaxPercentWithoutSignOff}% policy limit.");
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

    private static string GetVerdict(IReadOnlyCollection<DemoDailyStats> daily, decimal? revenuePerDiscount, DemoStore store)
    {
        if (daily.Count == 0)
        {
            return VerdictNoData;
        }

        var redemptionsPerDay = (double)daily.Sum(x => x.Redemptions) / daily.Count;
        var expectedPerDay = Math.Max(MinExpectedRedemptionsPerDay, (store?.OrdersPerDay ?? DefaultOrdersPerDay) * ExpectedReachShare);
        if (daily.Count >= VerdictMinDays && redemptionsPerDay < expectedPerDay)
        {
            return VerdictWeak;
        }

        return revenuePerDiscount switch
        {
            >= StrongRevenuePerDiscount => VerdictStrong,
            >= OkRevenuePerDiscount => VerdictOk,
            null => VerdictOk,
            _ => VerdictWeak,
        };
    }

    private static string DaysLeft(DemoPromotion promotion, DateTime now)
    {
        return promotion.EndDate == null ? "no end date" : $"{Math.Max(0, (int)Math.Ceiling((promotion.EndDate.Value - now).TotalDays))} days";
    }

    // The inputs every issue check needs, computed once per promotion.
    private sealed record IssueCheck(DemoPromotion Promotion, string Status, PromotionPerformance Performance, DateTime Now)
    {
        public PromotionIssue Issue(string severity, string code, string message, string action) =>
            new(severity, code, Promotion.Id, Promotion.Name, Promotion.StoreId, message, action);
    }
}
