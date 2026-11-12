using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpPagingExtensions
{
    // The paging of a list tool's call from its optional skip and take arguments: a negative skip reads as 0, and take
    // goes through the page size the platform allows.
    public static async Task<McpPaging> GetPagingAsync(this IMcpPageSizeResolver pageSizeResolver, int? skip, int? take)
    {
        ArgumentNullException.ThrowIfNull(pageSizeResolver);

        var pageSize = await pageSizeResolver.GetPageSizeAsync(take);

        return new McpPaging(Math.Max(skip ?? 0, 0), pageSize);
    }

    // Sets the skip and take of a module's search criteria.
    public static TCriteria WithPaging<TCriteria>(this TCriteria criteria, McpPaging paging)
        where TCriteria : SearchCriteriaBase
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(paging);

        criteria.Skip = paging.Skip;
        criteria.Take = paging.Take;

        return criteria;
    }

    // A page of a module's search result, each item mapped to the compact row the tool returns.
    public static McpPage<TRow> ToMcpPage<TItem, TRow>(this GenericSearchResult<TItem> result, McpPaging paging, Func<TItem, TRow> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return McpPage<TRow>.Create((result?.Results ?? []).Select(map), result?.TotalCount ?? 0, paging);
    }

    // A page of items already loaded, each mapped to its row.
    public static McpPage<TRow> ToMcpPage<TItem, TRow>(this IEnumerable<TItem> items, McpPaging paging, Func<TItem, TRow> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return McpPage<TRow>.FromList((items ?? []).Select(map), paging);
    }
}
