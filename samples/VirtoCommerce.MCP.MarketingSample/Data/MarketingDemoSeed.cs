using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.MarketingSample.Data;

// The demo data set. Every promotion is there to show something a marketer cares about; the comment on each says what.
public class MarketingDemoSeed
{
    public const string ElectronicsStoreId = "Electronics";
    public const string B2BStoreId = "B2B-store";
    public const string FashionStoreId = "Fashion";

    private const string CouponAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public List<DemoStore> Stores { get; } = [];

    public List<DemoPromotion> Promotions { get; } = [];

    public List<DemoSegment> Segments { get; } = [];

    public List<DemoCoupon> Coupons { get; } = [];

    public static MarketingDemoSeed Create(DateTime now)
    {
        var seed = new MarketingDemoSeed();
        var today = now.Date;
        var blackFriday = RetailCalendar.GetNextBlackFriday(now);
        var cyberMonday = blackFriday.AddDays(3);
        var december = new DateTime(blackFriday.Year, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        seed.Stores.AddRange(
        [
            new DemoStore { Id = ElectronicsStoreId, Name = "Electronics Retail", Currency = "USD", Languages = ["en-US", "de-DE", "fr-FR"], AverageOrderValue = 186m, OrdersPerDay = 420, MonthlyDiscountBudget = 250_000m },
            new DemoStore { Id = B2BStoreId, Name = "B2B Office Supply", Kind = RetailCalendar.B2B, Currency = "USD", Languages = ["en-US", "de-DE"], AverageOrderValue = 2_350m, OrdersPerDay = 55, MonthlyDiscountBudget = 120_000m },
            new DemoStore { Id = FashionStoreId, Name = "Fashion Outlet", Currency = "EUR", Languages = ["de-DE", "en-US"], AverageOrderValue = 92m, OrdersPerDay = 310, MonthlyDiscountBudget = 40_000m },
        ]);

        seed.Segments.AddRange(
        [
            new DemoSegment { Id = "vip", Name = "VIP members", Description = "Loyalty members with 5+ orders in 12 months.", Size = 4_210, StoreIds = [ElectronicsStoreId, FashionStoreId] },
            new DemoSegment { Id = "new-customers", Name = "New customers", Description = "Shoppers without a previous order.", Size = 18_900, StoreIds = [ElectronicsStoreId, FashionStoreId] },
            new DemoSegment { Id = "lapsed-90d", Name = "Lapsed 90 days", Description = "Customers without an order in the last 90 days.", Size = 9_840, StoreIds = [ElectronicsStoreId, FashionStoreId] },
            new DemoSegment { Id = "wholesale", Name = "Wholesale accounts", Description = "B2B organizations on contract pricing.", Size = 312, StoreIds = [B2BStoreId] },
            new DemoSegment { Id = "smb", Name = "Small business", Description = "B2B organizations with fewer than 50 employees.", Size = 1_480, StoreIds = [B2BStoreId] },
        ]);

        // Strong performer that ends in two days: the agent should suggest extending it.
        seed.Add(new DemoPromotion
        {
            Id = "autumn-laptops", StoreId = ElectronicsStoreId, Campaign = "Autumn 2026", Owner = "Emma Clarke",
            Name = "Autumn laptop sale: 15% off laptops",
            Description = "15% off all laptops, no code needed.",
            LocalizedNames = { ["en-US"] = "Autumn laptop sale: 15% off", ["de-DE"] = "Herbst-Laptop-Sale: 15 % Rabatt", ["fr-FR"] = "Soldes d'automne : -15 % sur les portables" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-18), EndDate = today.AddDays(2).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 15, TargetType = TargetType.Category, TargetCategories = ["Laptops"],
            Priority = 10, DiscountBudget = 100_000m, DailyRedemptions = 38, PromotionAverageOrderValue = 1_140m,
        });

        // Evergreen free shipping threshold.
        seed.Add(new DemoPromotion
        {
            Id = "free-shipping-99", StoreId = ElectronicsStoreId, Campaign = "Always on", Owner = "Emma Clarke",
            Name = "Free shipping on orders over $99",
            LocalizedNames = { ["en-US"] = "Free shipping over $99", ["de-DE"] = "Kostenloser Versand ab 99 $", ["fr-FR"] = "Livraison gratuite dès 99 $" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-240),
            RewardType = RewardType.FreeShipping, TargetType = TargetType.Shipping, MinOrderAmount = 99m,
            Priority = 1, DailyRedemptions = 160, PromotionAverageOrderValue = 214m,
        });

        // Redundant with the free shipping threshold for VIP members: a conflict to find.
        seed.Add(new DemoPromotion
        {
            Id = "vip-free-shipping", StoreId = ElectronicsStoreId, Campaign = "Loyalty", Owner = "Noah Fischer",
            Name = "VIP members: free shipping, no minimum",
            LocalizedNames = { ["en-US"] = "VIP: free shipping", ["de-DE"] = "VIP: Kostenloser Versand", ["fr-FR"] = "VIP : livraison gratuite" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-120), CustomerSegmentId = "vip",
            RewardType = RewardType.FreeShipping, TargetType = TargetType.Shipping,
            Priority = 1, DailyRedemptions = 22, PromotionAverageOrderValue = 248m,
        });

        // Above the 25% policy limit without sign-off, and 86% of its budget is spent.
        seed.Add(new DemoPromotion
        {
            Id = "headphones-flash", StoreId = ElectronicsStoreId, Campaign = "Flash sales", Owner = "Noah Fischer",
            Name = "Flash sale: 30% off headphones",
            LocalizedNames = { ["en-US"] = "Flash sale: 30% off headphones", ["de-DE"] = "Blitzangebot: 30 % auf Kopfhörer" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-3), EndDate = today.AddDays(4).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 30, TargetType = TargetType.Category, TargetCategories = ["Audio"],
            Priority = 20, DiscountBudget = 5_000m, DailyRedemptions = 69, PromotionAverageOrderValue = 132m,
        });

        // The big one: scheduled, missing its French name, overlapping the gift guide draft.
        seed.Add(new DemoPromotion
        {
            Id = "black-friday-doorbusters", StoreId = ElectronicsStoreId, Campaign = $"Black Friday {blackFriday.Year}", Owner = "Emma Clarke",
            Name = "Black Friday doorbusters: 20% off laptops, monitors and audio",
            LocalizedNames = { ["en-US"] = "Black Friday doorbusters", ["de-DE"] = "Black Friday Knaller" },
            State = PromotionState.Enabled, StartDate = blackFriday, EndDate = cyberMonday.AddDays(1).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 20, TargetType = TargetType.Category, TargetCategories = ["Laptops", "Monitors", "Audio"],
            Priority = 50, IsExclusive = true, DiscountBudget = 120_000m, DailyRedemptions = 420, PromotionAverageOrderValue = 610m,
        });

        // Needs a code, but no coupons were generated yet.
        seed.Add(new DemoPromotion
        {
            Id = "cyber-monday-50", StoreId = ElectronicsStoreId, Campaign = $"Black Friday {blackFriday.Year}", Owner = "Emma Clarke",
            Name = "Cyber Monday: $50 off orders over $400 with a code",
            LocalizedNames = { ["en-US"] = "Cyber Monday: $50 off $400+", ["de-DE"] = "Cyber Monday: 50 $ Rabatt ab 400 $", ["fr-FR"] = "Cyber Monday : 50 $ de remise dès 400 $" },
            State = PromotionState.Draft, StartDate = cyberMonday, EndDate = cyberMonday.AddDays(1).AddTicks(-1),
            RewardType = RewardType.AmountOff, RewardValue = 50, TargetType = TargetType.Cart, MinOrderAmount = 400m, CouponRequired = true,
            Priority = 40, MaxRedemptions = 5_000, DailyRedemptions = 900, PromotionAverageOrderValue = 520m,
        });

        // Over, but 120 of its single-use codes are still active.
        seed.Add(new DemoPromotion
        {
            Id = "back-to-school", StoreId = ElectronicsStoreId, Campaign = "Back to school", Owner = "Noah Fischer",
            Name = "Back to school: 10% off accessories",
            LocalizedNames = { ["en-US"] = "Back to school: 10% off accessories", ["de-DE"] = "Schulstart: 10 % auf Zubehör", ["fr-FR"] = "Rentrée : -10 % sur les accessoires" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-75), EndDate = today.AddDays(-40).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 10, TargetType = TargetType.Category, TargetCategories = ["Accessories"], CouponRequired = true,
            Priority = 10, DailyRedemptions = 5, PromotionAverageOrderValue = 74m,
        });

        // Evergreen welcome offer with one public code.
        seed.Add(new DemoPromotion
        {
            Id = "welcome-10", StoreId = ElectronicsStoreId, Campaign = "Acquisition", Owner = "Emma Clarke",
            Name = "Welcome offer: 10% off the first order",
            LocalizedNames = { ["en-US"] = "Welcome: 10% off your first order", ["de-DE"] = "Willkommen: 10 % auf die erste Bestellung", ["fr-FR"] = "Bienvenue : -10 % sur la première commande" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-400), CustomerSegmentId = "new-customers", CouponRequired = true,
            RewardType = RewardType.PercentOff, RewardValue = 10, TargetType = TargetType.Cart,
            Priority = 5, DailyRedemptions = 14, PromotionAverageOrderValue = 158m,
        });

        // Running for weeks with weak results: a candidate to pause or rework.
        seed.Add(new DemoPromotion
        {
            Id = "monitor-bundle", StoreId = ElectronicsStoreId, Campaign = "Autumn 2026", Owner = "Noah Fischer",
            Name = "Buy a monitor, get 50% off a second one",
            LocalizedNames = { ["en-US"] = "Second monitor at half price", ["de-DE"] = "Zweiter Monitor zum halben Preis", ["fr-FR"] = "Deuxième écran à moitié prix" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-25), EndDate = today.AddDays(20).AddTicks(-1),
            RewardType = RewardType.BuyXGetY, RewardValue = 1, TargetType = TargetType.Category, TargetCategories = ["Monitors"],
            Priority = 10, DailyRedemptions = 1.2, PromotionAverageOrderValue = 410m,
        });

        // Draft for December, without its German name.
        seed.Add(new DemoPromotion
        {
            Id = "holiday-gift-guide", StoreId = ElectronicsStoreId, Campaign = $"Holidays {december.Year}", Owner = "Emma Clarke",
            Name = "Holiday gift guide: 15% off gift picks",
            LocalizedNames = { ["en-US"] = "Holiday gift guide: 15% off", ["fr-FR"] = "Guide cadeaux : -15 %" },
            State = PromotionState.Draft, StartDate = december, EndDate = december.AddDays(23).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 15, TargetType = TargetType.Category, TargetCategories = ["Audio", "Accessories", "Smart home"],
            Priority = 20, DiscountBudget = 35_000m, DailyRedemptions = 95, PromotionAverageOrderValue = 165m,
        });

        // B2B: exclusive volume discount, which hides the printer promotion on large orders.
        seed.Add(new DemoPromotion
        {
            Id = "b2b-volume-5", StoreId = B2BStoreId, Campaign = "Contract", Owner = "Liam Novak",
            Name = "Volume discount: 5% off orders over $5,000",
            LocalizedNames = { ["en-US"] = "Volume discount 5%", ["de-DE"] = "Mengenrabatt 5 %" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-365), CustomerSegmentId = "wholesale",
            RewardType = RewardType.PercentOff, RewardValue = 5, TargetType = TargetType.Cart, MinOrderAmount = 5_000m,
            Priority = 100, IsExclusive = true, DailyRedemptions = 6, PromotionAverageOrderValue = 8_900m,
        });

        seed.Add(new DemoPromotion
        {
            Id = "b2b-printers-q4", StoreId = B2BStoreId, Campaign = "Q4 refresh", Owner = "Liam Novak",
            Name = "Q4 printer refresh: 12% off office printers",
            LocalizedNames = { ["en-US"] = "Q4 printer refresh: 12% off", ["de-DE"] = "Drucker-Aktion Q4: 12 % Rabatt" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-10), EndDate = today.AddDays(50).AddTicks(-1), CouponRequired = true,
            RewardType = RewardType.PercentOff, RewardValue = 12, TargetType = TargetType.Category, TargetCategories = ["Printers"],
            Priority = 30, DiscountBudget = 18_000m, DailyRedemptions = 3.5, PromotionAverageOrderValue = 3_200m,
        });

        // Paused by the team because of a stock shortage.
        seed.Add(new DemoPromotion
        {
            Id = "b2b-networking-200", StoreId = B2BStoreId, Campaign = "Q4 refresh", Owner = "Liam Novak",
            Name = "Networking gear: $200 off orders over $2,500",
            LocalizedNames = { ["en-US"] = "Networking: $200 off $2,500+", ["de-DE"] = "Netzwerktechnik: 200 $ Rabatt ab 2.500 $" },
            State = PromotionState.Paused, StartDate = today.AddDays(-30), EndDate = today.AddDays(30).AddTicks(-1),
            PauseReason = "Switches out of stock until the next delivery", PausedDate = today.AddDays(-5),
            RewardType = RewardType.AmountOff, RewardValue = 200, TargetType = TargetType.Category, TargetCategories = ["Networking"], MinOrderAmount = 2_500m,
            Priority = 30, DailyRedemptions = 2.2, PromotionAverageOrderValue = 4_100m,
        });

        // Exclusive with the same priority as the volume discount: which one wins is undefined.
        seed.Add(new DemoPromotion
        {
            Id = "b2b-year-end-8", StoreId = B2BStoreId, Campaign = "Year-end", Owner = "Liam Novak",
            Name = "Year-end budget flush: 8% off everything",
            LocalizedNames = { ["en-US"] = "Year-end: 8% off everything", ["de-DE"] = "Jahresende: 8 % auf alles" },
            State = PromotionState.Enabled, StartDate = december, EndDate = december.AddMonths(1).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 8, TargetType = TargetType.Cart, CustomerSegmentId = "smb",
            Priority = 100, IsExclusive = true, DiscountBudget = 30_000m, DailyRedemptions = 9, PromotionAverageOrderValue = 2_600m,
        });

        // Fashion: a healthy running promotion.
        seed.Add(new DemoPromotion
        {
            Id = "fashion-winter-coats", StoreId = FashionStoreId, Campaign = "Winter", Owner = "Mia Schneider",
            Name = "Winter coats: 20% off",
            LocalizedNames = { ["de-DE"] = "Wintermäntel: 20 % Rabatt", ["en-US"] = "Winter coats: 20% off" },
            State = PromotionState.Enabled, StartDate = today.AddDays(-7), EndDate = today.AddDays(30).AddTicks(-1),
            RewardType = RewardType.PercentOff, RewardValue = 20, TargetType = TargetType.Category, TargetCategories = ["Outerwear"],
            Priority = 10, DiscountBudget = 15_000m, DailyRedemptions = 48, PromotionAverageOrderValue = 168m,
        });

        // Starts in five days without its English name.
        seed.Add(new DemoPromotion
        {
            Id = "fashion-boots-3for2", StoreId = FashionStoreId, Campaign = "Winter", Owner = "Mia Schneider",
            Name = "Boots: buy 2, get the 3rd free",
            LocalizedNames = { ["de-DE"] = "Stiefel: 3 für 2" },
            State = PromotionState.Enabled, StartDate = today.AddDays(5), EndDate = today.AddDays(26).AddTicks(-1),
            RewardType = RewardType.BuyXGetY, RewardValue = 1, TargetType = TargetType.Category, TargetCategories = ["Footwear"],
            Priority = 10, DailyRedemptions = 20, PromotionAverageOrderValue = 230m,
        });

        seed.AddCoupons("welcome-10", ["WELCOME10"], maxUses: null, usedShare: 0, today.AddDays(-400), expiration: null);
        seed.Coupons.Single(x => x.Code == "WELCOME10").Uses = 5_612;
        seed.AddCoupons("back-to-school", GenerateCodes("BTS", 300, 11), maxUses: 1, usedShare: 0.6, today.AddDays(-76), expiration: null);
        seed.AddCoupons("b2b-printers-q4", GenerateCodes("PRN", 50, 23), maxUses: 1, usedShare: 0.24, today.AddDays(-11), expiration: today.AddDays(50));

        return seed;
    }

    public static IEnumerable<string> GenerateCodes(string prefix, int count, int seed)
    {
        var random = new Random(seed);
        var codes = new HashSet<string>();

        while (codes.Count < count)
        {
            codes.Add($"{prefix}-{new string(Enumerable.Range(0, 6).Select(_ => CouponAlphabet[random.Next(CouponAlphabet.Length)]).ToArray())}");
        }

        return codes;
    }

    private void Add(DemoPromotion promotion)
    {
        promotion.CreatedDate = (promotion.StartDate ?? DateTime.UtcNow.Date).AddDays(-14);
        Promotions.Add(DemoPromotion.Create(promotion));
    }

    private void AddCoupons(string promotionId, IEnumerable<string> codes, int? maxUses, double usedShare, DateTime createdDate, DateTime? expiration)
    {
        var list = codes.ToList();
        var used = (int)(list.Count * usedShare);

        Coupons.AddRange(list.Select((code, index) => new DemoCoupon
        {
            Code = code,
            PromotionId = promotionId,
            MaxUses = maxUses,
            Uses = index < used ? 1 : 0,
            CreatedDate = createdDate,
            ExpirationDate = expiration,
        }));
    }
}
