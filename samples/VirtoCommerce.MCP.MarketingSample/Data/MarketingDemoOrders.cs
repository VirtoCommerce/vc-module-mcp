using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.MarketingSample.Data;

public class DemoOrderLine
{
    public string Category { get; set; }

    public string Product { get; set; }

    public decimal Amount { get; set; }
}

public class DemoOrder
{
    public string Number { get; set; }

    public string StoreId { get; set; }

    public DateTime Date { get; set; }

    public string CustomerId { get; set; }

    public string[] CustomerSegments { get; set; } = [];

    public bool IsFirstOrder { get; set; }

    public string CouponCode { get; set; }

    public List<DemoOrderLine> Lines { get; set; } = [];

    public decimal Subtotal => Lines.Sum(x => x.Amount);
}

// The last 30 days of orders per store, generated with a fixed seed, so simulate_promotion and
// explain_promotion_evaluation work on order history that looks like a real store's. A real module would read
// customer orders with the Orders module search service.
public static class MarketingDemoOrders
{
    public const string ComplaintOrderNumber = "CO-10482";

    // Categories with their share of order lines: most baskets hold cheap accessories, few hold a laptop.
    private static readonly Dictionary<string, (string Category, int Weight, string[] Products, decimal Min, decimal Max)[]> _catalog = new()
    {
        [MarketingDemoSeed.ElectronicsStoreId] =
        [
            ("Accessories", 40, ["USB-C dock", "Logitech MX Master 3S", "Laptop sleeve", "Webcam 4K"], 19, 189),
            ("Audio", 20, ["Sony WH-1000XM6", "Bose QuietComfort Ultra", "AirPods Pro"], 129, 449),
            ("Smart home", 15, ["Nest Thermostat", "Philips Hue starter kit", "Ring doorbell"], 59, 279),
            ("Monitors", 15, ["Dell UltraSharp 27", "LG 32UN880", "Samsung Odyssey G7"], 249, 899),
            ("Laptops", 10, ["ThinkPad X1 Carbon", "MacBook Air 13", "Dell XPS 14", "HP EliteBook 840"], 899, 2199),
        ],
        [MarketingDemoSeed.B2BStoreId] =
        [
            ("Paper & supplies", 40, ["Copy paper, 5 boxes", "Toner pack", "Label printer rolls"], 49, 640),
            ("Office furniture", 20, ["Ergonomic chair", "Standing desk", "Meeting table"], 290, 1900),
            ("Printers", 15, ["HP LaserJet Enterprise", "Brother MFC-L8900", "Canon imageRUNNER"], 649, 3200),
            ("Laptops", 15, ["ThinkPad T14", "Latitude 5450"], 980, 1650),
            ("Networking", 10, ["Cisco Catalyst 9200", "Ubiquiti UniFi switch", "Meraki access point"], 399, 4800),
        ],
        [MarketingDemoSeed.FashionStoreId] =
        [
            ("Knitwear", 30, ["Merino sweater", "Cashmere scarf", "Cardigan"], 39, 189),
            ("Accessories", 30, ["Leather belt", "Gloves", "Beanie"], 19, 79),
            ("Footwear", 20, ["Chelsea boots", "Leather sneakers", "Winter boots"], 69, 229),
            ("Outerwear", 20, ["Wool coat", "Down parka", "Trench coat"], 129, 389),
        ],
    };

    public static IReadOnlyList<string> GetCategories(string storeId)
    {
        return _catalog.TryGetValue(storeId ?? string.Empty, out var catalog) ? catalog.Select(x => x.Category).ToList() : [];
    }

    private static readonly Dictionary<string, (int OrdersPerDay, string[] Segments)> _profiles = new()
    {
        [MarketingDemoSeed.ElectronicsStoreId] = (26, ["vip", "lapsed-90d"]),
        [MarketingDemoSeed.B2BStoreId] = (7, ["wholesale", "smb"]),
        [MarketingDemoSeed.FashionStoreId] = (18, ["vip", "lapsed-90d"]),
    };

    public static List<DemoOrder> Create(DateTime now)
    {
        var random = new Random(20261030);
        var orders = new List<DemoOrder>();
        // Generated orders start at CO-20000, so they never take the number of the complaint order below.
        var number = 20_000;

        for (var day = 30; day >= 1; day--)
        {
            foreach (var (storeId, profile) in _profiles)
            {
                for (var i = 0; i < profile.OrdersPerDay; i++)
                {
                    var order = new DemoOrder
                    {
                        Number = $"CO-{number++}",
                        StoreId = storeId,
                        Date = now.Date.AddDays(-day).AddMinutes(random.Next(8 * 60, 22 * 60)),
                        CustomerId = $"c-{random.Next(1, 4000):D4}",
                        IsFirstOrder = random.NextDouble() < 0.16,
                        CustomerSegments = profile.Segments.Where(_ => random.NextDouble() < 0.22).ToArray(),
                    };

                    var catalog = _catalog[storeId];
                    var lineCount = random.Next(1, storeId == MarketingDemoSeed.B2BStoreId ? 5 : 4);
                    for (var line = 0; line < lineCount; line++)
                    {
                        var (category, _, products, min, max) = PickCategory(catalog, random);
                        order.Lines.Add(new DemoOrderLine
                        {
                            Category = category,
                            Product = products[random.Next(products.Length)],
                            Amount = Math.Round(min + (max - min) * (decimal)random.NextDouble(), 2),
                        });
                    }

                    order.CouponCode = PickCoupon(order, random);
                    orders.Add(order);
                }
            }
        }

        // The order from the support ticket: WELCOME10 did not apply, because the customer had ordered before.
        orders.Add(new DemoOrder
        {
            Number = ComplaintOrderNumber,
            StoreId = MarketingDemoSeed.ElectronicsStoreId,
            Date = now.Date.AddDays(-2).AddHours(19).AddMinutes(12),
            CustomerId = "c-anna-k",
            IsFirstOrder = false,
            CouponCode = "WELCOME10",
            Lines =
            [
                new DemoOrderLine { Category = "Audio", Product = "Sony WH-1000XM6", Amount = 379m },
                new DemoOrderLine { Category = "Accessories", Product = "USB-C dock", Amount = 129m },
            ],
        });

        return orders;
    }

    private static (string Category, int Weight, string[] Products, decimal Min, decimal Max) PickCategory(
        (string Category, int Weight, string[] Products, decimal Min, decimal Max)[] catalog, Random random)
    {
        var roll = random.Next(catalog.Sum(x => x.Weight));
        foreach (var entry in catalog)
        {
            roll -= entry.Weight;
            if (roll < 0)
            {
                return entry;
            }
        }

        return catalog[^1];
    }

    private static string PickCoupon(DemoOrder order, Random random)
    {
        if (order.StoreId == MarketingDemoSeed.ElectronicsStoreId && order.IsFirstOrder && random.NextDouble() < 0.7)
        {
            return "WELCOME10";
        }

        // Customers also try codes that do not apply to them.
        if (order.StoreId == MarketingDemoSeed.ElectronicsStoreId && !order.IsFirstOrder && random.NextDouble() < 0.03)
        {
            return "WELCOME10";
        }

        return null;
    }
}
