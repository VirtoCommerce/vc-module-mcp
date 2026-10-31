using System;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.MarketingExtensionSample.Models;
using VirtoCommerce.MCP.MarketingExtensionSample.Services;
using VirtoCommerce.MCP.MarketingExtensionSample.Tools;
using VirtoCommerce.MCP.MarketingSample;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Tests;

// The extension wired the way the platform wires it, with a fixed date so the seeded data is the same on every run.
public sealed class ContosoTestContext
{
    public static readonly DateTimeOffset Today = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    static ContosoTestContext()
    {
        AbstractTypeFactory<DemoPromotion>.OverrideType<DemoPromotion, ContosoPromotion>();
    }

    public ContosoTestContext()
    {
        Store = new MarketingDemoStore(new FixedTimeProvider(Today));
        Options = Microsoft.Extensions.Options.Options.Create(new ContosoMarketingOptions());
        Analytics = new ContosoMarketingAnalytics(Store, Options);
        Access = new MarketingAccess(Store);
        Tools = new ContosoFinanceTools(Store, Access, Analytics, Options);
    }

    public MarketingDemoStore Store { get; }

    public IOptions<ContosoMarketingOptions> Options { get; }

    public ContosoMarketingAnalytics Analytics { get; }

    public MarketingAccess Access { get; }

    public ContosoFinanceTools Tools { get; }

    public static ClaimsPrincipal User(params string[] permissions)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Name, "contoso-marketer"));
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(PlatformConstants.Security.Claims.PermissionClaimType, permission));
        }

        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal Marketer() =>
        User(MarketingSampleConstants.ReadPermission, MarketingSampleConstants.UpdatePermission);

    public static ClaimsPrincipal FashionMarketer() =>
        User(MarketingSampleConstants.ReadPermission + "|[{\"Type\":\"MarketingStoreSelectedScope\",\"Scope\":\"Fashion\"}]",
            MarketingSampleConstants.UpdatePermission);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
