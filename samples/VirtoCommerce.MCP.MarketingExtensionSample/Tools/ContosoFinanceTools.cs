using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.MarketingExtensionSample.Models;
using VirtoCommerce.MCP.MarketingExtensionSample.Services;
using VirtoCommerce.MCP.MarketingSample;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Tools;

// New tools in the same admin scope as the sample's tools. They reuse the sample's services (store access, analysis)
// instead of copying them, and follow its conventions: [McpPermission] with store scopes, dryRun with a diff.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
public partial class ContosoFinanceTools(
    MarketingDemoStore store,
    MarketingAccess access,
    MarketingAnalytics analytics,
    IOptions<ContosoMarketingOptions> options)
{
    [McpServerTool(Name = "set_promotion_cost_center", ReadOnly = false, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [Description("Book a promotion's discount to a Contoso finance cost center, such as CC-4100. Promotions with a large " +
        "discount budget need one before they go live.")]
    public object SetPromotionCostCenter(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("Cost center: CC- followed by four digits, for example CC-4100.")] string cost_center,
        bool dryRun = false)
    {
        return SetCostCenter(context.User, promotion_id, cost_center, dryRun);
    }

    [McpServerTool(Name = "get_margin_floors", ReadOnly = true, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.ReadPermission)]
    [Description("Contoso's margin floors: the largest percent discount per category that keeps the margin above the floor. " +
        "Check it before proposing a discount, and the promotions that break it.")]
    public object GetMarginFloors(
        RequestContext<CallToolRequestParams> context,
        [Description("One category, for example Laptops. Omit it for all.")] string category = null)
    {
        return GetFloors(context.User, category);
    }

    // The logic of the tools takes the user directly, so unit tests call it without an MCP request.
    public McpChangePreview SetCostCenter(ClaimsPrincipal user, string promotionId, string costCenter, bool dryRun)
    {
        if (string.IsNullOrEmpty(costCenter) || !CostCenterRegex().IsMatch(costCenter))
        {
            throw new McpException("cost_center must be CC- followed by four digits, for example CC-4100.");
        }

        var promotion = access.GetUpdatablePromotion(user, promotionId);
        var target = (ContosoPromotion)promotion.Clone();
        target.CostCenter = costCenter.ToUpperInvariant();

        var preview = new McpChangePreview
        {
            Action = "set_cost_center",
            ObjectType = "promotion",
            ObjectId = promotion.Id,
            ObjectName = promotion.Name,
            DryRun = dryRun,
        };
        preview.AddChange("cost_center", (promotion as ContosoPromotion)?.CostCenter, target.CostCenter);
        foreach (var warning in analytics.CheckPolicy(target))
        {
            preview.AddWarning(warning);
        }

        if (!dryRun)
        {
            store.SavePromotion(target, user.GetUserName());
        }

        return preview;
    }

    public object GetFloors(ClaimsPrincipal user, string category)
    {
        var stores = access.GetReadableStores(user).Select(x => x.Id).ToHashSet();
        var contosoAnalytics = analytics as ContosoMarketingAnalytics;

        return new
        {
            margin_floors = options.Value.MaxPercentByCategory
                .Where(x => string.IsNullOrEmpty(category) || string.Equals(x.Key, category, System.StringComparison.OrdinalIgnoreCase))
                .Select(x => new { category = x.Key, max_percent_off = x.Value }),
            promotions_below_floor = store.GetPromotions()
                .Where(x => stores.Contains(x.StoreId) && x.GetStatus(store.Now) is not PromotionStatus.Expired)
                .SelectMany(x => (contosoAnalytics?.GetMarginFloorBreaches(x) ?? []).Select(breach => new
                {
                    promotion_id = x.Id,
                    name = x.Name,
                    status = x.GetStatus(store.Now),
                    breach.Category,
                    percent_off = x.RewardValue,
                    max_percent_off = breach.MaxPercent,
                }))
                .Where(x => string.IsNullOrEmpty(category) || string.Equals(x.Category, category, System.StringComparison.OrdinalIgnoreCase)),
        };
    }

    [GeneratedRegex("^CC-[0-9]{4}$", RegexOptions.IgnoreCase)]
    private static partial Regex CostCenterRegex();
}
