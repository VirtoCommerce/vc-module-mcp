using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.Core.Models;

// The skip and take of one call of a list tool: skip is never negative, take is the page size the platform allows
// (IMcpPageSizeResolver), so one call cannot load more rows than MCP.Tools.MaxTake.
public sealed record McpPaging(int Skip, int Take);

// One page of a list tool's answer. The total and the next skip tell the agent that there is more and how to ask for
// it, so an answer is never cut off silently. Serialized as items, total_count, skip, take, next_skip and has_more.
public class McpPage<T>
{
    public IList<T> Items { get; set; } = [];

    public int TotalCount { get; set; }

    public int Skip { get; set; }

    public int Take { get; set; }

    // The skip of the next page, or null on the last page.
    public int? NextSkip => Skip + Items.Count < TotalCount && Items.Count > 0 ? Skip + Items.Count : null;

    public bool HasMore => NextSkip != null;

    public static McpPage<T> Create(IEnumerable<T> items, int totalCount, McpPaging paging)
    {
        ArgumentNullException.ThrowIfNull(paging);

        return new McpPage<T>
        {
            Items = items?.ToList() ?? [],
            TotalCount = totalCount,
            Skip = paging.Skip,
            Take = paging.Take,
        };
    }

    // A page of a list held in memory: the items of the page and the count of the whole list.
    public static McpPage<T> FromList(IEnumerable<T> all, McpPaging paging)
    {
        ArgumentNullException.ThrowIfNull(paging);

        var list = all?.ToList() ?? [];

        return Create(list.Skip(paging.Skip).Take(paging.Take), list.Count, paging);
    }
}
