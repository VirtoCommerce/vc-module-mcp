using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Tests.Testing;

public sealed class FixtureStore
{
    public List<string> Items { get; } = Enumerable.Range(1, 25).Select(x => $"item-{x}").ToList();

    public List<string> Deleted { get; } = [];

    public List<string> Paused { get; } = [];

    public int GeneratedCodes { get; set; }
}

// A small toolset that follows every convention, for McpToolHarness and McpToolsetConventions.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
public sealed class FixtureTools(FixtureStore store)
{
    public const string ReadPermission = "items:read";
    public const string UpdatePermission = "items:update";
    public const string StoreScopeType = "ItemStoreSelectedScope";

    [McpServerTool(Name = "search_items", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Search items. Returns one page of item ids with the total.")]
    [Authorize(ReadPermission)]
    public async Task<McpPage<string>> SearchItems(
        IMcpPageSizeResolver pageSizeResolver,
        [Description("Items to skip, from 0.")] int? skip = null,
        [Description("Items to return, up to the platform's limit.")] int? take = null)
    {
        var paging = await pageSizeResolver.GetPagingAsync(skip, take);
        return McpPage<string>.FromList(store.Items, paging);
    }

    [McpServerTool(Name = "get_store_report", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("The report of one store the user manages.")]
    [McpPermission(ReadPermission)]
    public object GetStoreReport(RequestContext<CallToolRequestParams> context, [Description("Store id.")] string store_id)
    {
        var grant = context.User.GetMcpPermissionGrant(ReadPermission);
        if (!grant.Allows(StoreScopeType, store_id))
        {
            throw new McpForbiddenException($"You do not manage store '{store_id}'.");
        }

        return new { store_id, items = store.Items.Count };
    }

    [McpServerTool(Name = "delete_item", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Delete an item. Submitted for approval.")]
    [Authorize(UpdatePermission)]
    [McpRequiresApproval]
    public McpChangePreview DeleteItem([Description("Item id.")] string item_id, [Description("Preview only.")] bool dryRun = false)
    {
        if (!dryRun)
        {
            store.Deleted.Add(item_id);
        }

        return new McpChangePreview { Action = "delete", ObjectType = "item", ObjectId = item_id, DryRun = dryRun };
    }

    [McpServerTool(Name = "pause_item", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Pause an item at once. Asks the user to confirm.")]
    [Authorize(UpdatePermission)]
    [McpRequiresConfirmation("Pause {item_id} now?")]
    public McpChangePreview PauseItem([Description("Item id.")] string item_id, [Description("Preview only.")] bool dryRun = false)
    {
        if (!dryRun)
        {
            store.Paused.Add(item_id);
        }

        return new McpChangePreview { Action = "pause", ObjectType = "item", ObjectId = item_id, DryRun = dryRun };
    }

    [McpServerTool(Name = "generate_codes", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Description("Generate codes. More than 10 codes are submitted for approval.")]
    [Authorize(UpdatePermission)]
    [McpRequiresApproval(Condition = nameof(NeedsApproval))]
    public object GenerateCodes([Description("Number of codes.")] int count, [Description("Preview only.")] bool dryRun = false)
    {
        if (!dryRun)
        {
            store.GeneratedCodes += count;
        }

        return new { count, dry_run = dryRun };
    }

    public static bool NeedsApproval(IReadOnlyDictionary<string, JsonElement> arguments) =>
        !arguments.TryGetValue("count", out var count) || count.ValueKind != JsonValueKind.Number || count.GetInt32() > 10;

    [McpServerTool(Name = "check_health", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Checks the items. Fails when there is nothing to check.")]
    [McpAnyUser]
    public object CheckHealth([Description("Fail on purpose.")] bool fail = false)
    {
        if (fail)
        {
            throw new McpException("Nothing to check.");
        }

        return new { ok = true };
    }
}

// Tools that break the conventions on purpose, one rule each.
[McpServerToolType]
public sealed class BrokenTools
{
    [McpServerTool]
    [Description("No name.")]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    public static string Unnamed() => "x";

    [McpServerTool(Name = "SearchThings", ReadOnly = true)]
    [Description("Upper case name.")]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    public static string UpperCase([Description("Skip.")] int? skip = null, [Description("Take.")] int? take = null) => "x";

    [McpServerTool(Name = "get_things", ReadOnly = true)]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    public static string NoDescription(string query) => query;

    [McpServerTool(Name = "describe_things", ReadOnly = true)]
    [Description("No scope and no access.")]
    public static string NoScopeNoAccess() => "x";

    [McpServerTool(Name = "list_things", ReadOnly = true)]
    [Description("A list without paging.")]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    public static string NoPaging() => "x";

    [McpServerTool(Name = "find_things", ReadOnly = false, Destructive = false)]
    [Description("A reading verb that is not ReadOnly.")]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    public static string NotReadOnly([Description("Preview only.")] bool dryRun = false) => "x";

    [McpServerTool(Name = "wipe_things", ReadOnly = false, Destructive = true)]
    [Description("Destroys without a safeguard or a dry run.")]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    public static string Wipe() => "x";

    [McpServerTool(Name = "grant_things", ReadOnly = false, Destructive = false)]
    [Description("An approval condition that does not exist, a confirmation that names an unknown argument.")]
    [McpScope(McpScopes.Admin)]
    [McpAnyUser]
    [McpRequiresApproval(Condition = "Missing")]
    [McpRequiresConfirmation("Grant {who}?")]
    public static string Grant([Description("Preview only.")] bool dryRun = false) => "x";
}

// A second tool named search_items in the same scope.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
public static class DuplicateTools
{
    [McpServerTool(Name = "search_items", ReadOnly = true)]
    [Description("A duplicate.")]
    [McpAnyUser]
    public static string SearchItems([Description("Skip.")] int? skip = null, [Description("Take.")] int? take = null) => "x";
}
