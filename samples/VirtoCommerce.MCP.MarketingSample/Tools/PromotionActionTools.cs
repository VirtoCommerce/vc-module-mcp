using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// Layer 4, intent-level actions: the verbs a marketer uses, not entity saves. Each takes dryRun and returns a diff
// (McpChangePreview). What gives money away (going live, a longer run, a bigger discount) waits for an approver;
// what limits risk (pause, a draft, a translation) applies at once.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
public partial class PromotionActionTools(
    MarketingDemoStore store,
    MarketingAccess access,
    MarketingAnalytics analytics,
    PromotionEvaluator evaluator)
{
    private const int SimulationDays = 30;

    private const string ObjectType = "promotion";

    private static readonly string[] _rewardTypes = [RewardType.PercentOff, RewardType.AmountOff, RewardType.FreeShipping, RewardType.BuyXGetY];
    private static readonly string[] _targetTypes = [TargetType.Category, TargetType.Cart, TargetType.Shipping];

    [McpServerTool(Name = "create_promotion_draft", ReadOnly = false, Destructive = false, Idempotent = false)]
    [McpPermission(MarketingSampleConstants.CreatePermission, MarketingSampleConstants.ReadPermission)]
    [Description("Create a promotion as a draft. A draft is not visible to customers, so it needs no approval; " +
        "schedule_promotion puts it live after approval. Returns the draft with policy warnings and conflicts to fix first.")]
    public object CreatePromotionDraft(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id.")] string store_id,
        [Description("Internal name, for example 'Black Friday: 20% off monitors'.")] string name,
        [Description("percent_off, amount_off, free_shipping or buy_x_get_y.")] string reward_type,
        [Description("Percent for percent_off, amount in the store currency for amount_off, number of free items for buy_x_get_y; 0 for free_shipping.")] decimal reward_value,
        [Description("category, cart or shipping.")] string target_type,
        [Description("Categories for target_type category, for example ['Laptops', 'Monitors'].")] string[] categories = null,
        [Description("First day, yyyy-MM-dd.")] DateTime? start_date = null,
        [Description("Last day, yyyy-MM-dd.")] DateTime? end_date = null,
        [Description("Minimum order amount in the store currency.")] decimal? min_order_amount = null,
        [Description("Customer segment id from list_customer_segments.")] string customer_segment_id = null,
        [Description("Whether customers need a coupon code.")] bool coupon_required = false,
        [Description("Customer-facing names by language, for example {\"en-US\": \"20% off monitors\", \"de-DE\": \"20 % auf Monitore\"}.")] Dictionary<string, string> names = null,
        [Description("Campaign name.")] string campaign = null,
        [Description("Discount budget in the store currency.")] decimal? discount_budget = null,
        bool dryRun = false)
    {
        var demoStore = access.GetReadableStore(context.User, store_id);
        MarketingAccess.EnsureAllowed(context.User, MarketingSampleConstants.CreatePermission, demoStore.Id);

        ValidateOffer(reward_type, reward_value, target_type, categories);
        if (customer_segment_id != null && store.GetSegment(customer_segment_id)?.StoreIds.Contains(demoStore.Id) != true)
        {
            throw new McpException($"Segment '{customer_segment_id}' is not available in store '{demoStore.Id}'. Use list_customer_segments.");
        }

        var promotion = DemoPromotion.Create(new DemoPromotion
        {
            Id = CreateId(name),
            StoreId = demoStore.Id,
            Name = name,
            State = PromotionState.Draft,
            StartDate = MarketingFormat.StartOfDay(start_date),
            EndDate = MarketingFormat.EndOfDay(end_date),
            RewardType = reward_type,
            RewardValue = reward_value,
            TargetType = target_type,
            TargetCategories = categories ?? [],
            MinOrderAmount = min_order_amount,
            CustomerSegmentId = customer_segment_id,
            CouponRequired = coupon_required,
            Campaign = campaign,
            DiscountBudget = discount_budget,
            Owner = context.User.GetUserName(),
            Priority = 10,
            CreatedDate = store.Now,
            LocalizedNames = new Dictionary<string, string>(names ?? [], StringComparer.OrdinalIgnoreCase),
            DailyRedemptions = 10,
            PromotionAverageOrderValue = demoStore.AverageOrderValue * 1.1m,
        });

        ValidateDates(promotion.StartDate, promotion.EndDate);

        var preview = new McpChangePreview
        {
            Action = "create_draft",
            ObjectType = ObjectType,
            ObjectId = promotion.Id,
            ObjectName = promotion.Name,
            DryRun = dryRun,
        };
        preview.AddChange("status", null, PromotionStatus.Draft)
            .AddChange("offer", null, MarketingFormat.Offer(promotion))
            .AddChange("dates", null, $"{MarketingFormat.Date(promotion.StartDate) ?? "not set"} to {MarketingFormat.Date(promotion.EndDate) ?? "not set"}");
        AddUnknownCategoriesWarning(preview, promotion);
        AddWarnings(preview, promotion);

        if (!dryRun)
        {
            store.SavePromotion(promotion, context.User.GetUserName());
        }

        return preview;
    }

    [McpServerTool(Name = "schedule_promotion", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [McpRequiresApproval]
    [Description("Put a draft or paused promotion live for a period: it starts automatically on the first day. " +
        "The change is submitted for approval; tell the user the change id and that an approver must approve it. " +
        "Check find_promotion_conflicts first.")]
    public object SchedulePromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("First day, yyyy-MM-dd.")] DateTime start_date,
        [Description("Last day, yyyy-MM-dd.")] DateTime end_date,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        var now = store.Now;
        var start = MarketingFormat.StartOfDay(start_date);
        var end = MarketingFormat.EndOfDay(end_date);
        ValidateDates(start, end);

        return Apply(context.User, promotion, "schedule", dryRun, (preview, target) =>
        {
            preview.AddChange("status", promotion.GetStatus(now), start > now ? PromotionStatus.Scheduled : PromotionStatus.Active)
                .AddChange("start", MarketingFormat.Date(promotion.StartDate), MarketingFormat.Date(start))
                .AddChange("end", MarketingFormat.Date(promotion.EndDate), MarketingFormat.Date(end));

            target.State = PromotionState.Enabled;
            target.StartDate = start;
            target.EndDate = end;
            target.PauseReason = null;
            target.PausedDate = null;
            AddSimulation(preview, target);
        });
    }

    [McpServerTool(Name = "activate_promotion", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [McpRequiresApproval]
    [Description("Resume a paused promotion, or put a draft live with the dates it has. The change is submitted for approval.")]
    public object ActivatePromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        var now = store.Now;
        var status = promotion.GetStatus(now);
        if (status is not (PromotionStatus.Paused or PromotionStatus.Draft))
        {
            throw new McpException($"Promotion '{promotion.Id}' is {status}; only a paused promotion or a draft can be activated.");
        }

        if (promotion.EndDate < now)
        {
            throw new McpException($"Promotion '{promotion.Id}' ended {MarketingFormat.Date(promotion.EndDate)}. Use schedule_promotion with new dates.");
        }

        return Apply(context.User, promotion, "activate", dryRun, (preview, target) =>
        {
            target.State = PromotionState.Enabled;
            target.PauseReason = null;
            target.PausedDate = null;
            preview.AddChange("status", status, target.GetStatus(now));
            AddSimulation(preview, target);
        });
    }

    [McpServerTool(Name = "pause_promotion", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [Description("Pause a live or scheduled promotion right away, for example when the stock runs out or the budget is spent. " +
        "Pausing limits risk, so it needs no approval; activate_promotion resumes it after approval.")]
    public object PausePromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("Why it is paused, shown to the team.")] string reason,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        var now = store.Now;
        var status = promotion.GetStatus(now);
        if (status is not (PromotionStatus.Active or PromotionStatus.Scheduled))
        {
            throw new McpException($"Promotion '{promotion.Id}' is {status}; only an active or scheduled promotion can be paused.");
        }

        return Apply(context.User, promotion, "pause", dryRun, (preview, target) =>
        {
            target.State = PromotionState.Paused;
            target.PauseReason = reason;
            target.PausedDate = now;
            preview.AddChange("status", status, PromotionStatus.Paused).AddChange("pause_reason", null, reason);
        });
    }

    [McpServerTool(Name = "extend_promotion", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [McpRequiresApproval]
    [Description("Move the last day of a promotion later. The preview estimates the extra discount cost from the recent results. " +
        "The change is submitted for approval.")]
    public object ExtendPromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("New last day, yyyy-MM-dd.")] DateTime end_date,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        var end = MarketingFormat.EndOfDay(end_date);
        if (promotion.EndDate != null && end <= promotion.EndDate)
        {
            throw new McpException($"The new last day must be after the current one, {MarketingFormat.Date(promotion.EndDate)}.");
        }

        var recent = analytics.GetPerformance(promotion, 7);
        var extraDays = Math.Ceiling(((end ?? store.Now) - (promotion.EndDate ?? store.Now)).TotalDays);
        var estimatedCost = recent.Days > 0 ? Math.Round(recent.DiscountCost / recent.Days * (decimal)extraDays, 0) : 0;

        return Apply(context.User, promotion, "extend", dryRun, (preview, target) =>
        {
            preview.AddChange("end", MarketingFormat.Date(promotion.EndDate), MarketingFormat.Date(end));
            if (estimatedCost > 0)
            {
                preview.AddWarning($"Estimated extra discount cost: about {estimatedCost:N0} {store.GetStore(promotion.StoreId)?.Currency} for {extraDays} more days, at the pace of the last 7 days.");
            }

            if (promotion.DiscountBudget > 0 && analytics.GetPerformance(promotion).DiscountCost + estimatedCost > promotion.DiscountBudget)
            {
                preview.AddWarning($"The extension would go over the discount budget of {promotion.DiscountBudget:N0}.");
            }

            target.EndDate = end;
        });
    }

    [McpServerTool(Name = "change_promotion_discount", ReadOnly = false, Destructive = true, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [McpRequiresApproval]
    [Description("Change the discount of a promotion: the percent for percent_off, the amount for amount_off. " +
        "The preview shows the policy check. The change is submitted for approval.")]
    public object ChangePromotionDiscount(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("New percent or amount.")] decimal reward_value,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        if (promotion.RewardType is not (RewardType.PercentOff or RewardType.AmountOff))
        {
            throw new McpException($"The discount of a {promotion.RewardType} promotion cannot be changed with this tool.");
        }

        ValidateOffer(promotion.RewardType, reward_value, promotion.TargetType, promotion.TargetCategories);

        return Apply(context.User, promotion, "change_discount", dryRun, (preview, target) =>
        {
            target.RewardValue = reward_value;
            preview.AddChange("offer", MarketingFormat.Offer(promotion), MarketingFormat.Offer(target));
        });
    }

    [McpServerTool(Name = "set_promotion_translation", ReadOnly = false, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [Description("Set the customer-facing name of a promotion for one store language, for example a missing de-DE or fr-FR name.")]
    public object SetPromotionTranslation(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("Language of the store, for example de-DE.")] string language,
        [Description("Customer-facing name in that language.")] string name,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        var demoStore = store.GetStore(promotion.StoreId);
        if (!demoStore.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            throw new McpException($"Store '{demoStore.Id}' has no language '{language}'. Its languages: {string.Join(", ", demoStore.Languages)}.");
        }

        return Apply(context.User, promotion, "set_translation", dryRun, (preview, target) =>
        {
            promotion.LocalizedNames.TryGetValue(language, out var oldName);
            target.LocalizedNames[language] = name;
            preview.AddChange($"name ({language})", oldName, name);
        });
    }

    [McpServerTool(Name = "update_promotion_draft", ReadOnly = false, Destructive = false, Idempotent = true)]
    [McpPermission(MarketingSampleConstants.UpdatePermission, MarketingSampleConstants.ReadPermission)]
    [Description("Change a draft or a paused promotion: name, discount, categories, conditions, dates or budget. Only the " +
        "arguments you pass change. A live or scheduled promotion is not edited in place: pause it first, so its history stays honest.")]
    public object UpdatePromotionDraft(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id.")] string promotion_id,
        [Description("New internal name.")] string name = null,
        [Description("New percent or amount.")] decimal? reward_value = null,
        [Description("New categories for a category promotion.")] string[] categories = null,
        [Description("New minimum order amount; 0 removes it.")] decimal? min_order_amount = null,
        [Description("New customer segment id; an empty string removes it.")] string customer_segment_id = null,
        [Description("Whether customers need a coupon code.")] bool? coupon_required = null,
        [Description("New first day, yyyy-MM-dd.")] DateTime? start_date = null,
        [Description("New last day, yyyy-MM-dd.")] DateTime? end_date = null,
        [Description("New discount budget.")] decimal? discount_budget = null,
        bool dryRun = false)
    {
        var promotion = access.GetUpdatablePromotion(context.User, promotion_id);
        var status = promotion.GetStatus(store.Now);
        if (status is not (PromotionStatus.Draft or PromotionStatus.Paused))
        {
            throw new McpException($"Promotion '{promotion.Id}' is {status}. Only a draft or a paused promotion can be edited; pause it first with pause_promotion.");
        }

        if (reward_value != null || categories != null)
        {
            ValidateOffer(promotion.RewardType, reward_value ?? promotion.RewardValue, promotion.TargetType, categories ?? promotion.TargetCategories);
        }

        if (!string.IsNullOrEmpty(customer_segment_id) && store.GetSegment(customer_segment_id)?.StoreIds.Contains(promotion.StoreId) != true)
        {
            throw new McpException($"Segment '{customer_segment_id}' is not available in store '{promotion.StoreId}'. Use list_customer_segments.");
        }

        var start = start_date != null ? MarketingFormat.StartOfDay(start_date) : promotion.StartDate;
        var end = end_date != null ? MarketingFormat.EndOfDay(end_date) : promotion.EndDate;
        ValidateDates(start, end);

        return Apply(context.User, promotion, "update_draft", dryRun, (preview, target) =>
        {
            target.Name = name ?? target.Name;
            target.RewardValue = reward_value ?? target.RewardValue;
            target.TargetCategories = categories ?? target.TargetCategories;
            target.MinOrderAmount = min_order_amount == null ? target.MinOrderAmount : min_order_amount > 0 ? min_order_amount : null;
            target.CustomerSegmentId = customer_segment_id == null ? target.CustomerSegmentId : customer_segment_id.Length > 0 ? customer_segment_id : null;
            target.CouponRequired = coupon_required ?? target.CouponRequired;
            target.StartDate = start;
            target.EndDate = end;
            target.DiscountBudget = discount_budget ?? target.DiscountBudget;

            preview.AddChange("name", promotion.Name, target.Name)
                .AddChange("offer", MarketingFormat.Offer(promotion), MarketingFormat.Offer(target))
                .AddChange("start", MarketingFormat.Date(promotion.StartDate), MarketingFormat.Date(target.StartDate))
                .AddChange("end", MarketingFormat.Date(promotion.EndDate), MarketingFormat.Date(target.EndDate))
                .AddChange("discount_budget", promotion.DiscountBudget, target.DiscountBudget);
        });
    }

    [McpServerTool(Name = "clone_promotion", ReadOnly = false, Destructive = false, Idempotent = false)]
    [McpPermission(MarketingSampleConstants.CreatePermission, MarketingSampleConstants.ReadPermission)]
    [Description("Copy a promotion as a new draft, optionally for another store and shifted in time: 'the same as Black Friday, " +
        "but for the Fashion store, one week later'. The copy is a draft, so it needs no approval.")]
    public object ClonePromotion(
        RequestContext<CallToolRequestParams> context,
        [Description("Promotion id to copy.")] string promotion_id,
        [Description("Store of the copy. Defaults to the store of the original.")] string store_id = null,
        [Description("Days to move the dates by, for example 7. Defaults to 0.")] int shift_days = 0,
        [Description("Name of the copy. Defaults to the original name.")] string name = null,
        bool dryRun = false)
    {
        var source = access.GetReadablePromotion(context.User, promotion_id);
        var targetStore = access.GetReadableStore(context.User, store_id ?? source.StoreId);
        MarketingAccess.EnsureAllowed(context.User, MarketingSampleConstants.CreatePermission, targetStore.Id);

        var copy = source.Clone();
        copy.Id = CreateId(name ?? source.Name);
        copy.Name = name ?? source.Name;
        copy.StoreId = targetStore.Id;
        copy.State = PromotionState.Draft;
        copy.StartDate = source.StartDate?.AddDays(shift_days);
        copy.EndDate = source.EndDate?.AddDays(shift_days);
        copy.PauseReason = null;
        copy.PausedDate = null;
        copy.Owner = context.User.GetUserName();
        copy.CreatedDate = store.Now;
        copy.LocalizedNames = source.LocalizedNames
            .Where(x => targetStore.Languages.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (copy.CustomerSegmentId != null && store.GetSegment(copy.CustomerSegmentId)?.StoreIds.Contains(targetStore.Id) != true)
        {
            copy.CustomerSegmentId = null;
        }

        var preview = new McpChangePreview
        {
            Action = "clone",
            ObjectType = ObjectType,
            ObjectId = copy.Id,
            ObjectName = copy.Name,
            DryRun = dryRun,
        };
        preview.AddChange("copy_of", null, source.Id)
            .AddChange("store", null, copy.StoreId)
            .AddChange("dates", null, $"{MarketingFormat.Date(copy.StartDate) ?? "not set"} to {MarketingFormat.Date(copy.EndDate) ?? "not set"}")
            .AddChange("status", null, PromotionStatus.Draft);
        if (source.CustomerSegmentId != null && copy.CustomerSegmentId == null)
        {
            preview.AddWarning($"Segment {source.CustomerSegmentId} does not exist in store {copy.StoreId}; the copy is for every customer.");
        }

        AddUnknownCategoriesWarning(preview, copy);

        if (targetStore.Currency != store.GetStore(source.StoreId)?.Currency && source.RewardType == RewardType.AmountOff)
        {
            preview.AddWarning($"The amount {source.RewardValue} was in {store.GetStore(source.StoreId)?.Currency}; the copy uses {targetStore.Currency}. Check it with update_promotion_draft.");
        }

        AddWarnings(preview, copy);

        if (!dryRun)
        {
            store.SavePromotion(copy, context.User.GetUserName());
        }

        return preview;
    }

    // What the promotion would have cost on the store's recent orders, shown to the approver before it goes live.
    protected virtual void AddSimulation(McpChangePreview preview, DemoPromotion promotion)
    {
        var orders = store.GetOrders(promotion.StoreId, store.Now.Date.AddDays(-SimulationDays));
        var discounts = orders
            .Select(order => evaluator.Evaluate(promotion, order, ignoreSchedule: true, assumeCoupon: true))
            .Where(x => x.Qualifies)
            .Select(x => x.Discount)
            .ToList();

        if (orders.Count > 0)
        {
            var assumption = promotion.CouponRequired ? ", if every order used a code (an upper bound)" : string.Empty;
            preview.AddWarning($"Simulation on the last {SimulationDays} days of orders{assumption}: {100m * discounts.Count / orders.Count:0.#}% of orders qualify, " +
                $"about {discounts.Sum():N0} {store.GetStore(promotion.StoreId)?.Currency} of discount per {SimulationDays} days.");
        }
    }

    private static void AddUnknownCategoriesWarning(McpChangePreview preview, DemoPromotion promotion)
    {
        var known = MarketingDemoOrders.GetCategories(promotion.StoreId);
        var unknown = promotion.TargetCategories.Where(x => !known.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            preview.AddWarning($"Store {promotion.StoreId} sells no {string.Join(", ", unknown)}; its categories are {string.Join(", ", known)}. Change them with update_promotion_draft.");
        }
    }

    // Builds the diff on a copy, checks the result against the policy and the other promotions, and saves it unless dryRun.
    private McpChangePreview Apply(ClaimsPrincipal user, DemoPromotion promotion, string action, bool dryRun, Action<McpChangePreview, DemoPromotion> change)
    {
        var target = promotion.Clone();
        var preview = new McpChangePreview
        {
            Action = action,
            ObjectType = ObjectType,
            ObjectId = promotion.Id,
            ObjectName = promotion.Name,
            DryRun = dryRun,
        };

        change(preview, target);

        // Pausing removes the risk, so the policy and the conflicts of the promotion do not matter for it.
        if (target.State != PromotionState.Paused)
        {
            AddWarnings(preview, target);
        }

        if (!dryRun)
        {
            store.SavePromotion(target, user.GetUserName());
        }

        return preview;
    }

    private void AddWarnings(McpChangePreview preview, DemoPromotion promotion)
    {
        foreach (var warning in analytics.CheckPolicy(promotion))
        {
            preview.AddWarning(warning);
        }

        foreach (var conflict in store.GetPromotions().Select(x => analytics.GetConflict(promotion, x)).Where(x => x != null))
        {
            var other = conflict.PromotionIds[0] == promotion.Id ? 1 : 0;
            preview.AddWarning($"Conflict ({conflict.Kind}) with '{conflict.PromotionNames[other]}' ({conflict.PromotionIds[other]}): {conflict.Message}");
        }
    }

    private static void ValidateOffer(string rewardType, decimal rewardValue, string targetType, IReadOnlyCollection<string> categories)
    {
        if (!_rewardTypes.Contains(rewardType))
        {
            throw new McpException($"reward_type must be one of: {string.Join(", ", _rewardTypes)}.");
        }

        if (!_targetTypes.Contains(targetType))
        {
            throw new McpException($"target_type must be one of: {string.Join(", ", _targetTypes)}.");
        }

        if (targetType == TargetType.Category && (categories == null || categories.Count == 0))
        {
            throw new McpException("A category promotion needs at least one category.");
        }

        if (rewardValue < 0 || (rewardType == RewardType.PercentOff && rewardValue > MarketingSampleConstants.MaxPercent))
        {
            throw new McpException($"The discount must be between 0 and {MarketingSampleConstants.MaxPercent}% for percent_off. The discount policy does not allow more.");
        }
    }

    private void ValidateDates(DateTime? start, DateTime? end)
    {
        if (start != null && end != null && end < start)
        {
            throw new McpException("The last day must not be before the first day.");
        }

        if (end != null && end < store.Now)
        {
            throw new McpException($"The last day {MarketingFormat.Date(end)} is in the past.");
        }
    }

    private string CreateId(string name)
    {
        var slug = SlugRegex().Replace(name?.ToLowerInvariant() ?? "promotion", "-").Trim('-');
        slug = slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;

        var id = slug;
        for (var i = 2; store.GetPromotion(id) != null; i++)
        {
            id = $"{slug}-{i}";
        }

        return id;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
