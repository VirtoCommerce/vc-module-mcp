using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.MarketingSample.Data;

public class DemoPlaceholder
{
    public string StoreId { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public string Page { get; set; }
}

public class DemoContentItem
{
    public string Id { get; set; }

    public string Name { get; set; }

    public string Folder { get; set; }

    // banner, html_block or product_carousel.
    public string ContentType { get; set; }

    public string Headline { get; set; }

    public string Body { get; set; }

    public string PromotionId { get; set; }

    public string[] Languages { get; set; } = [];

    public DateTime ModifiedDate { get; set; }
}

// An item shown in a placeholder of a store for a period, optionally for one customer segment only.
public class DemoPublication
{
    public string Id { get; set; }

    public string StoreId { get; set; }

    public string Placeholder { get; set; }

    public string ItemId { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public string SegmentId { get; set; }

    public int Priority { get; set; }

    public bool IsLive(DateTime date) => StartDate <= date && date <= EndDate;

    public DemoPublication Clone() => (DemoPublication)MemberwiseClone();
}

// In-memory stand-in for the Content module's placeholders, content items and publications (the marketing content
// toolset). Seeded relative to today: the autumn hero ends without a successor, the checkout upsell runs out next
// week, the loyalty banner targets a segment that no longer exists. A real module would use the Content module services.
public class ContentDemoStore
{
    public const string RemovedSegmentId = "gold-partners";

    private readonly object _lock = new();
    private readonly List<DemoPlaceholder> _placeholders = [];
    private readonly Dictionary<string, DemoContentItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DemoPublication> _publications = new(StringComparer.OrdinalIgnoreCase);
    private int _nextPublication = 1;

    public ContentDemoStore(MarketingDemoStore marketing)
    {
        Marketing = marketing;
        Seed(marketing.Now);
    }

    public MarketingDemoStore Marketing { get; }

    public DateTime Now => Marketing.Now;

    public IReadOnlyList<DemoPlaceholder> GetPlaceholders(string storeId)
    {
        return _placeholders.Where(x => storeId == null || string.Equals(x.StoreId, storeId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public DemoPlaceholder GetPlaceholder(string storeId, string name)
    {
        return _placeholders.FirstOrDefault(x => string.Equals(x.StoreId, storeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<DemoContentItem> GetItems()
    {
        lock (_lock)
        {
            return [.. _items.Values];
        }
    }

    public DemoContentItem GetItem(string id)
    {
        lock (_lock)
        {
            return id != null && _items.TryGetValue(id, out var item) ? item : null;
        }
    }

    public void SaveItem(DemoContentItem item)
    {
        lock (_lock)
        {
            item.ModifiedDate = Now;
            _items[item.Id] = item;
        }
    }

    public IReadOnlyList<DemoPublication> GetPublications(string storeId = null)
    {
        lock (_lock)
        {
            return _publications.Values
                .Where(x => storeId == null || string.Equals(x.StoreId, storeId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Clone())
                .ToList();
        }
    }

    public DemoPublication GetPublication(string id)
    {
        lock (_lock)
        {
            return id != null && _publications.TryGetValue(id, out var publication) ? publication.Clone() : null;
        }
    }

    public string NewPublicationId()
    {
        lock (_lock)
        {
            return $"pub-{_nextPublication++:D3}";
        }
    }

    public void SavePublication(DemoPublication publication)
    {
        lock (_lock)
        {
            _publications[publication.Id] = publication.Clone();
        }
    }

    public bool DeletePublication(string id)
    {
        lock (_lock)
        {
            return _publications.Remove(id);
        }
    }

    private void Seed(DateTime now)
    {
        var today = now.Date;
        var blackFriday = RetailCalendar.GetNextBlackFriday(now);
        var cyberMonday = blackFriday.AddDays(3);
        var december = new DateTime(blackFriday.Year, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime EndOf(DateTime day) => day.Date.AddDays(1).AddTicks(-1);

        foreach (var (store, name, page, description) in new[]
        {
            (MarketingDemoSeed.ElectronicsStoreId, "home-hero", "Home", "Full-width hero banner at the top of the home page."),
            (MarketingDemoSeed.ElectronicsStoreId, "home-promo-strip", "Home", "Thin strip under the header with the current offer."),
            (MarketingDemoSeed.ElectronicsStoreId, "category-banner", "Category pages", "Banner above the product list of a category."),
            (MarketingDemoSeed.ElectronicsStoreId, "checkout-upsell", "Checkout", "Product carousel shown before payment."),
            (MarketingDemoSeed.ElectronicsStoreId, "account-dashboard", "My account", "Card on the customer's account dashboard."),
            (MarketingDemoSeed.B2BStoreId, "home-hero", "Home", "Hero banner of the B2B home page."),
            (MarketingDemoSeed.B2BStoreId, "account-dashboard", "My account", "Card on the buyer's account dashboard."),
            (MarketingDemoSeed.B2BStoreId, "quote-sidebar", "Quote request", "Sidebar next to the quote request form."),
            (MarketingDemoSeed.FashionStoreId, "home-hero", "Home", "Hero banner of the home page."),
            (MarketingDemoSeed.FashionStoreId, "lookbook", "Lookbook", "Editorial block on the lookbook page."),
            (MarketingDemoSeed.FashionStoreId, "checkout-upsell", "Checkout", "Accessories carousel shown before payment."),
        })
        {
            _placeholders.Add(new DemoPlaceholder { StoreId = store, Name = name, Page = page, Description = description });
        }

        void Item(string id, string name, string folder, string type, string headline, string promotionId, params string[] languages) =>
            _items[id] = new DemoContentItem
            {
                Id = id, Name = name, Folder = folder, ContentType = type, Headline = headline, PromotionId = promotionId,
                Languages = languages, ModifiedDate = today.AddDays(-12),
            };

        Item("hero-autumn-laptops", "Autumn laptop sale hero", "Banners/Autumn", "banner", "15% off every laptop. This week only.", "autumn-laptops", "en-US", "de-DE", "fr-FR");
        Item("hero-black-friday", "Black Friday hero", "Banners/Black Friday", "banner", "Doorbusters: up to 20% off laptops, monitors and audio.", "black-friday-doorbusters", "en-US", "de-DE");
        Item("strip-free-shipping", "Free shipping strip", "Strips", "html_block", "Free shipping on orders over $99.", "free-shipping-99", "en-US", "de-DE", "fr-FR");
        Item("upsell-accessories", "Accessories upsell", "Upsell", "product_carousel", "Complete your setup", null, "en-US", "de-DE", "fr-FR");
        Item("card-vip-loyalty", "VIP loyalty card", "Loyalty", "banner", "Your VIP perks: free shipping on every order.", "vip-free-shipping", "en-US");
        Item("hero-b2b-printers", "Q4 printer refresh hero", "Banners/B2B", "banner", "Refresh your office printers: 12% off.", "b2b-printers-q4", "en-US", "de-DE");
        Item("hero-fashion-winter", "Winter coats hero", "Banners/Winter", "banner", "Winter coats: 20% off.", "fashion-winter-coats", "de-DE", "en-US");
        Item("lookbook-winter", "Winter lookbook", "Editorial", "html_block", "Layers for the cold season.", null, "de-DE", "en-US");
        Item("hero-holiday-gifts", "Holiday gift guide hero", "Banners/Holidays", "banner", "Gifts they will love, 15% off.", "holiday-gift-guide", "en-US", "fr-FR");

        void Publish(string store, string placeholder, string item, DateTime start, DateTime end, string segment = null, int priority = 10)
        {
            var id = NewPublicationId();
            _publications[id] = new DemoPublication
            {
                Id = id, StoreId = store, Placeholder = placeholder, ItemId = item,
                StartDate = start, EndDate = end, SegmentId = segment, Priority = priority,
            };
        }

        // The autumn hero ends with the sale; nothing follows it until Black Friday.
        Publish(MarketingDemoSeed.ElectronicsStoreId, "home-hero", "hero-autumn-laptops", today.AddDays(-18), EndOf(today.AddDays(1)));
        Publish(MarketingDemoSeed.ElectronicsStoreId, "home-hero", "hero-black-friday", blackFriday, EndOf(cyberMonday), priority: 50);
        Publish(MarketingDemoSeed.ElectronicsStoreId, "home-promo-strip", "strip-free-shipping", today.AddDays(-240), EndOf(today.AddDays(365)));
        // The upsell runs out next week.
        Publish(MarketingDemoSeed.ElectronicsStoreId, "checkout-upsell", "upsell-accessories", today.AddDays(-30), EndOf(today.AddDays(4)));
        // Targets a customer group that was deleted: it can never be shown.
        Publish(MarketingDemoSeed.ElectronicsStoreId, "account-dashboard", "card-vip-loyalty", today.AddDays(-60), EndOf(today.AddDays(60)), segment: RemovedSegmentId);
        Publish(MarketingDemoSeed.B2BStoreId, "home-hero", "hero-b2b-printers", today.AddDays(-10), EndOf(today.AddDays(49)));
        Publish(MarketingDemoSeed.FashionStoreId, "home-hero", "hero-fashion-winter", today.AddDays(-7), EndOf(today.AddDays(29)));
        Publish(MarketingDemoSeed.FashionStoreId, "lookbook", "lookbook-winter", today.AddDays(-20), EndOf(today.AddDays(70)));
        Publish(MarketingDemoSeed.ElectronicsStoreId, "category-banner", "hero-holiday-gifts", december, EndOf(december.AddDays(23)));
    }
}
