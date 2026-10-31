using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Jobs;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// Layer 5, bulk: accept a list or a filter, dispatch the work to the job engine through IMcpJobDispatcher, and
// return a job handle with a preview of the first items. The agent follows the job with get_job_status.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
public partial class CouponBulkTools(
    MarketingDemoStore store,
    MarketingAccess access,
    IMcpJobDispatcher jobDispatcher)
{
    private const int PreviewSize = 10;

    [McpServerTool(Name = "generate_coupons", ReadOnly = false, Destructive = false, Idempotent = false)]
    [McpRequiresApproval(Condition = nameof(NeedsApproval))]
    [Description("Generate unique single-use (or multi-use) coupon codes for a promotion that needs a code, for example for a newsletter, " +
        "a partner, or one code for a customer. Runs as a background job and returns a job_id to follow with get_job_status. " +
        "More than 1,000 codes are submitted for approval first; the preview shows the first codes.")]
    public async Task<object> GenerateCoupons(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("Number of codes, 1 to 100000.")] int count,
        [Description("Code prefix, 2-10 letters or digits, for example CYBER. Defaults to the start of the promotion id.")] string prefix = null,
        [Description("Uses per code. Defaults to 1, a single-use code.")] int max_uses_per_code = 1,
        [Description("Last day the codes work, yyyy-MM-dd. Defaults to the end of the promotion.")] DateTime? expiration_date = null,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        if (count is < 1 or > MarketingSampleConstants.MaxCouponsPerJob)
        {
            throw new McpException($"count must be between 1 and {MarketingSampleConstants.MaxCouponsPerJob}.");
        }

        if (max_uses_per_code < 1)
        {
            throw new McpException("max_uses_per_code must be 1 or more.");
        }

        prefix = string.IsNullOrWhiteSpace(prefix) ? GetDefaultPrefix(promotion.Id) : prefix.Trim().ToUpperInvariant();
        if (!ValidPrefixRegex().IsMatch(prefix))
        {
            throw new McpException("prefix must be 2-10 letters or digits.");
        }

        var payload = new GenerateCouponsPayload
        {
            PromotionId = promotion.Id,
            Prefix = prefix,
            Count = count,
            MaxUses = max_uses_per_code,
            ExpirationDate = MarketingFormat.EndOfDay(expiration_date) ?? promotion.EndDate,
            Seed = StableHash($"{promotion.Id}|{prefix}|{count}|{store.GetCoupons(promotion.Id).Count}"),
        };
        var preview = GenerateCouponsJob.GetCodes(store, payload).Take(PreviewSize).ToList();
        var title = $"Generate {count:N0} coupons for {promotion.Name}";

        if (dryRun)
        {
            var change = new McpChangePreview
            {
                Action = "generate_coupons",
                ObjectType = "promotion",
                ObjectId = promotion.Id,
                ObjectName = promotion.Name,
                DryRun = true,
            };
            change.AddChange("coupons", store.GetCoupons(promotion.Id).Count, store.GetCoupons(promotion.Id).Count + count)
                .AddChange("first_codes", null, string.Join(", ", preview))
                .AddChange("uses_per_code", null, max_uses_per_code)
                .AddChange("expires", null, MarketingFormat.Date(payload.ExpirationDate) ?? "never");
            if (!promotion.CouponRequired)
            {
                change.AddWarning("The promotion does not require a code, so customers get the discount without these coupons.");
            }

            return change;
        }

        return await jobDispatcher.EnqueueAsync<GenerateCouponsJob>(context, payload, new McpJobRequest
        {
            Title = title,
            TotalCount = count,
            Preview = preview,
        });
    }

    // Approval condition of generate_coupons: a few codes, such as one for a complaint, need no second person.
    public static bool NeedsApproval(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        return !arguments.TryGetValue("count", out var count) || count.ValueKind != JsonValueKind.Number ||
            count.GetInt32() > MarketingSampleConstants.CouponsWithoutApproval;
    }

    [McpServerTool(Name = "deactivate_stale_coupons", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Deactivate the coupons that are still active although their promotion has ended, so old codes shared on " +
        "coupon sites stop working. Runs as a background job; call it with dryRun first to see how many and which.")]
    public async Task<object> DeactivateStaleCoupons(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null,
        bool dryRun = false)
    {
        var storeIds = access.ResolveStores(context.User, store_id).Select(x => x.Id).ToArray();
        foreach (var storeId in storeIds)
        {
            MarketingAccess.EnsureAllowed(context.User, MarketingSampleConstants.UpdatePermission, storeId);
        }

        var coupons = DeactivateStaleCouponsJob.FindStaleCoupons(store, storeIds);
        var preview = coupons
            .GroupBy(x => x.PromotionId)
            .Select(x => new { promotion_id = x.Key, coupons = x.Count(), first_codes = x.Take(3).Select(c => c.Code) })
            .ToList();

        if (dryRun || coupons.Count == 0)
        {
            return new
            {
                dry_run = dryRun,
                stale_coupons = coupons.Count,
                by_promotion = preview,
                message = coupons.Count == 0 ? "No stale coupons: nothing to do." : "Call again without dryRun to deactivate them.",
            };
        }

        return await jobDispatcher.EnqueueAsync<DeactivateStaleCouponsJob>(context, new DeactivateStaleCouponsPayload { StoreIds = storeIds }, new McpJobRequest
        {
            Title = $"Deactivate {coupons.Count} stale coupons",
            TotalCount = coupons.Count,
            Preview = preview,
        });
    }

    private static string GetDefaultPrefix(string promotionId)
    {
        var letters = NotAlphanumericRegex().Replace(promotionId.ToUpperInvariant(), string.Empty);
        return letters.Length >= 2 ? letters[..Math.Min(6, letters.Length)] : "PROMO";
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            return value.Aggregate(17, (hash, c) => hash * 31 + c);
        }
    }

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NotAlphanumericRegex();

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex ValidPrefixRegex();
}
