using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using ModelContextProtocol;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingSample.Services;

// Store scopes of the marketing permissions, enforced inside the tools: a marketer whose marketing:read is limited
// to some stores sees and changes the promotions of those stores only.
public class MarketingAccess(MarketingDemoStore store)
{
    public IReadOnlyList<DemoStore> GetReadableStores(ClaimsPrincipal user)
    {
        var grant = user.GetMcpPermissionGrant(MarketingSampleConstants.ReadPermission);
        return grant == null
            ? []
            : store.GetStores().Where(x => grant.Allows(MarketingSampleConstants.StoreScopeType, x.Id)).ToList();
    }

    // The stores to work with: the one asked for, or every store the user may read.
    public IReadOnlyList<DemoStore> ResolveStores(ClaimsPrincipal user, string storeId)
    {
        if (string.IsNullOrEmpty(storeId))
        {
            return GetReadableStores(user);
        }

        return [GetReadableStore(user, storeId)];
    }

    public DemoStore GetReadableStore(ClaimsPrincipal user, string storeId)
    {
        var demoStore = store.GetStore(storeId)
            ?? throw new McpException($"Store '{storeId}' was not found. Stores: {string.Join(", ", store.GetStores().Select(x => x.Id))}.");

        if (!GetReadableStores(user).Any(x => x.Id == demoStore.Id))
        {
            throw new McpForbiddenException($"You do not manage the marketing of store '{demoStore.Id}'.");
        }

        return demoStore;
    }

    public DemoPromotion GetReadablePromotion(ClaimsPrincipal user, string promotionId)
    {
        var promotion = store.GetPromotion(promotionId)
            ?? throw new McpException($"Promotion '{promotionId}' was not found. Use search_promotions to find its id.");

        GetReadableStore(user, promotion.StoreId);

        return promotion;
    }

    // A change needs the update permission for the store of the promotion, as well as read access to it.
    public DemoPromotion GetUpdatablePromotion(ClaimsPrincipal user, string promotionId)
    {
        var promotion = GetReadablePromotion(user, promotionId);
        EnsureAllowed(user, MarketingSampleConstants.UpdatePermission, promotion.StoreId);

        return promotion;
    }

    public static void EnsureAllowed(ClaimsPrincipal user, string permission, string storeId)
    {
        var grant = user.GetMcpPermissionGrant(permission);
        if (grant == null || !grant.Allows(MarketingSampleConstants.StoreScopeType, storeId))
        {
            throw new McpForbiddenException($"Your {permission} permission does not cover store '{storeId}'.");
        }
    }
}
