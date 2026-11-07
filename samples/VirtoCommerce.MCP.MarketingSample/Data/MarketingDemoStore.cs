using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.MarketingSample.Data;

// In-memory stand-in for the Marketing module, so the demo runs on any platform without touching real promotions.
// The data is seeded relative to the current date and the real retail calendar, so the demo always looks current:
// Black Friday is the next one, a laptop sale ends in two days, a flash sale is over its budget. Changes live until
// the platform restarts. A real module would call IPromotionSearchService, IPromotionService and ICouponService instead.
public class MarketingDemoStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DemoStore> _stores;
    private readonly Dictionary<string, DemoPromotion> _promotions;
    private readonly Dictionary<string, DemoSegment> _segments;
    private readonly List<DemoCoupon> _coupons;
    private readonly List<DemoOrder> _orders;

    public MarketingDemoStore(TimeProvider timeProvider)
    {
        TimeProvider = timeProvider;
        var seed = MarketingDemoSeed.Create(Now);

        _stores = seed.Stores.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        _promotions = seed.Promotions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        _segments = seed.Segments.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        _coupons = seed.Coupons;
        _orders = MarketingDemoOrders.Create(Now);
    }

    public TimeProvider TimeProvider { get; }

    public DateTime Now => TimeProvider.GetUtcNow().UtcDateTime;

    public IReadOnlyList<DemoStore> GetStores()
    {
        return [.. _stores.Values];
    }

    public DemoStore GetStore(string storeId)
    {
        return storeId != null && _stores.TryGetValue(storeId, out var store) ? store : null;
    }

    public IReadOnlyList<DemoSegment> GetSegments()
    {
        return [.. _segments.Values];
    }

    public DemoSegment GetSegment(string segmentId)
    {
        return segmentId != null && _segments.TryGetValue(segmentId, out var segment) ? segment : null;
    }

    public IReadOnlyList<DemoOrder> GetOrders(string storeId, DateTime from)
    {
        return _orders.Where(x => x.StoreId == storeId && x.Date >= from).ToList();
    }

    public DemoOrder GetOrder(string number)
    {
        return _orders.FirstOrDefault(x => string.Equals(x.Number, number, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<DemoPromotion> GetPromotions()
    {
        lock (_lock)
        {
            return _promotions.Values.Select(x => x.Clone()).ToList();
        }
    }

    public DemoPromotion GetPromotion(string promotionId)
    {
        lock (_lock)
        {
            return promotionId != null && _promotions.TryGetValue(promotionId, out var promotion) ? promotion.Clone() : null;
        }
    }

    public void SavePromotion(DemoPromotion promotion, string userName)
    {
        lock (_lock)
        {
            promotion.ModifiedDate = Now;
            promotion.ModifiedBy = userName;
            _promotions[promotion.Id] = promotion.Clone();
        }
    }

    public IReadOnlyList<DemoCoupon> GetCoupons(string promotionId = null)
    {
        lock (_lock)
        {
            return _coupons
                .Where(x => promotionId == null || string.Equals(x.PromotionId, promotionId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Clone())
                .ToList();
        }
    }

    public bool CouponExists(string code)
    {
        lock (_lock)
        {
            return _coupons.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddCoupons(IEnumerable<DemoCoupon> coupons)
    {
        lock (_lock)
        {
            _coupons.AddRange(coupons);
        }
    }

    public int DeactivateCoupons(IEnumerable<string> codes)
    {
        var set = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            var coupons = _coupons.Where(x => x.IsActive && set.Contains(x.Code)).ToList();
            coupons.ForEach(x => x.IsActive = false);
            return coupons.Count;
        }
    }

    // Daily results of a promotion, simulated from its inputs: stable between calls, and only for the days it ran.
    public IReadOnlyList<DemoDailyStats> GetDailyStats(DemoPromotion promotion)
    {
        if (promotion.StartDate == null || promotion.State == PromotionState.Draft || promotion.DailyRedemptions <= 0)
        {
            return [];
        }

        var store = GetStore(promotion.StoreId);
        var first = promotion.StartDate.Value.Date;
        var last = new[] { Now.Date.AddDays(-1), promotion.EndDate?.Date ?? DateTime.MaxValue, promotion.PausedDate?.Date.AddDays(-1) ?? DateTime.MaxValue }.Min();
        var random = new Random(StableHash(promotion.Id));
        var result = new List<DemoDailyStats>();

        for (var date = first; date <= last; date = date.AddDays(1))
        {
            // Weekends sell more in retail and less in B2B; every day varies by up to 30 percent.
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var seasonality = promotion.StoreId == MarketingDemoSeed.B2BStoreId ? (weekend ? 0.35 : 1.15) : (weekend ? 1.3 : 0.9);
            var redemptions = (int)Math.Round(promotion.DailyRedemptions * seasonality * (0.7 + random.NextDouble() * 0.6));
            var revenue = Math.Round(redemptions * promotion.PromotionAverageOrderValue * (decimal)(0.9 + random.NextDouble() * 0.2), 2);

            result.Add(new DemoDailyStats
            {
                Date = date,
                Redemptions = redemptions,
                Revenue = revenue,
                DiscountCost = Math.Round(GetDiscountCost(promotion, store, redemptions, revenue), 2),
            });
        }

        return result;
    }

    private static decimal GetDiscountCost(DemoPromotion promotion, DemoStore store, int redemptions, decimal revenue)
    {
        return promotion.RewardType switch
        {
            RewardType.PercentOff when promotion.TargetType == TargetType.Category => revenue * promotion.RewardValue / 100m * 0.6m,
            RewardType.PercentOff => revenue * promotion.RewardValue / 100m,
            RewardType.AmountOff => redemptions * promotion.RewardValue,
            RewardType.FreeShipping => redemptions * (store?.Currency == "EUR" ? 6.9m : 8.95m),
            RewardType.BuyXGetY => revenue * 0.25m,
            _ => 0m,
        };
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            return value.Aggregate(17, (hash, c) => hash * 31 + c);
        }
    }
}
