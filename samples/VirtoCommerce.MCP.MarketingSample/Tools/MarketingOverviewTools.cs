using System;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;

namespace VirtoCommerce.MCP.MarketingSample.Tools;

// Layer 1, orientation: "where am I, what is the state". Aggregated and opinionated, so the agent can start a
// conversation with one call instead of paging through promotions.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[McpPermission(MarketingSampleConstants.ReadPermission)]
public class MarketingOverviewTools(
    MarketingDemoStore store,
    MarketingAccess access,
    MarketingAnalytics analytics)
{
    // The windows and list sizes of the overview, also named in its field names (best_last_7_days, ...).
    private const int RecentDays = 7;
    private const int StartingSoonDays = 30;
    private const int TopCount = 3;
    private const int LookAheadDays = 45;

    [McpServerTool(Name = "get_marketing_overview", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Start here. The state of marketing in the stores the user manages: live, scheduled, draft and paused promotions, " +
        "discount spend this month against the budget, the best promotions of the last 7 days, promotions ending or starting soon, " +
        "the most important issues and the retail events ahead. Use it for a morning check or before planning.")]
    public object GetMarketingOverview(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null)
    {
        var now = store.Now;
        var stores = access.ResolveStores(context.User, store_id);
        var promotions = store.GetPromotions().Where(x => stores.Any(s => s.Id == x.StoreId)).ToList();
        var issues = analytics.FindIssues(promotions);

        return new
        {
            today = MarketingFormat.Date(now),
            stores = stores.Select(demoStore =>
            {
                var storePromotions = promotions.Where(x => x.StoreId == demoStore.Id).ToList();
                var storeIssues = issues.Where(x => x.StoreId == demoStore.Id).ToList();
                var spend = analytics.GetMonthToDateSpend(demoStore.Id);

                return new
                {
                    store_id = demoStore.Id,
                    name = demoStore.Name,
                    currency = demoStore.Currency,
                    promotions = new
                    {
                        active = storePromotions.Count(x => x.GetStatus(now) == PromotionStatus.Active),
                        scheduled = storePromotions.Count(x => x.GetStatus(now) == PromotionStatus.Scheduled),
                        drafts = storePromotions.Count(x => x.GetStatus(now) == PromotionStatus.Draft),
                        paused = storePromotions.Count(x => x.GetStatus(now) == PromotionStatus.Paused),
                    },
                    discount_spend_month_to_date = spend,
                    monthly_discount_budget = demoStore.MonthlyDiscountBudget,
                    budget_used_percent = MarketingFormat.Percent(spend / demoStore.MonthlyDiscountBudget),
                    best_last_7_days = storePromotions
                        .Where(x => x.GetStatus(now) == PromotionStatus.Active)
                        .Select(x => (Promotion: x, Performance: analytics.GetPerformance(x, RecentDays)))
                        .OrderByDescending(x => x.Performance.Revenue)
                        .Take(TopCount)
                        .Select(x => new
                        {
                            promotion_id = x.Promotion.Id,
                            name = x.Promotion.Name,
                            revenue = x.Performance.Revenue,
                            redemptions = x.Performance.Redemptions,
                            verdict = x.Performance.Verdict,
                        }),
                    ending_within_7_days = storePromotions
                        .Where(x => x.GetStatus(now) == PromotionStatus.Active && x.EndDate <= now.AddDays(RecentDays))
                        .OrderBy(x => x.EndDate)
                        .Select(x => new { promotion_id = x.Id, name = x.Name, end = MarketingFormat.Date(x.EndDate) }),
                    starting_within_30_days = storePromotions
                        .Where(x => x.GetStatus(now) is PromotionStatus.Scheduled or PromotionStatus.Draft && x.StartDate <= now.AddDays(StartingSoonDays))
                        .OrderBy(x => x.StartDate)
                        .Select(x => new { promotion_id = x.Id, name = x.Name, status = x.GetStatus(now), start = MarketingFormat.Date(x.StartDate) }),
                    issues = new
                    {
                        high = storeIssues.Count(x => x.Severity == MarketingAnalytics.SeverityHigh),
                        medium = storeIssues.Count(x => x.Severity == MarketingAnalytics.SeverityMedium),
                        low = storeIssues.Count(x => x.Severity == MarketingAnalytics.SeverityLow),
                        top = storeIssues.Take(TopCount).Select(x => new { x.Severity, x.Code, promotion_id = x.PromotionId, x.Message }),
                    },
                };
            }),
            upcoming_retail_events = RetailCalendar.GetEvents(now.Date, now.Date.AddDays(LookAheadDays))
                .Where(x => stores.Any(s => x.Concerns(s.Kind)))
                .Select(x => new { x.Name, start = MarketingFormat.Date(x.StartDate), end = MarketingFormat.Date(x.EndDate) }),
            next_steps = new[]
            {
                "find_promotion_issues lists every issue with a suggested action.",
                "find_promotion_conflicts checks promotions that overlap or stack.",
                "get_promotion_calendar shows the plan against the retail calendar.",
            },
        };
    }

    [McpServerTool(Name = "get_promotion_calendar", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("The promotion plan for a period next to the retail calendar (Black Friday, Cyber Monday, holidays): " +
        "which promotions run when, and which retail events have no promotion yet. Defaults to the next 45 days.")]
    public object GetPromotionCalendar(
        RequestContext<CallToolRequestParams> context,
        [Description("Store id. Omit it for every store the user manages.")] string store_id = null,
        [Description("First day, yyyy-MM-dd. Defaults to today.")] DateTime? from = null,
        [Description("Last day, yyyy-MM-dd. Defaults to 45 days from the first day.")] DateTime? to = null)
    {
        var now = store.Now;
        var start = from?.Date ?? now.Date;
        var end = MarketingFormat.EndOfDay(to ?? start.AddDays(LookAheadDays)).Value;
        if (end < start)
        {
            throw new ModelContextProtocol.McpException("The last day must not be before the first day.");
        }

        var stores = access.ResolveStores(context.User, store_id);
        var promotions = store.GetPromotions()
            .Where(x => stores.Any(s => s.Id == x.StoreId) && x.GetStatus(now) != PromotionStatus.Expired && x.Overlaps(start, end))
            .OrderBy(x => x.StartDate)
            .ToList();

        return new
        {
            from = MarketingFormat.Date(start),
            to = MarketingFormat.Date(end),
            promotions = promotions.Select(x => MarketingFormat.Row(x, now)),
            retail_events = RetailCalendar.GetEvents(start, end)
                .Where(x => stores.Any(s => x.Concerns(s.Kind)))
                .Select(retailEvent =>
                {
                    // Always-on promotions (no end date) do not count as covering an event.
                    var coveredBy = promotions
                        .Where(x => x.EndDate != null && stores.Any(s => s.Id == x.StoreId && retailEvent.Concerns(s.Kind)))
                        .Where(x => x.Overlaps(retailEvent.StartDate, MarketingFormat.EndOfDay(retailEvent.EndDate)))
                        .Select(x => $"{x.StoreId}: {x.Name} ({x.GetStatus(now)})")
                        .ToList();

                    return new
                    {
                        retailEvent.Name,
                        start = MarketingFormat.Date(retailEvent.StartDate),
                        end = MarketingFormat.Date(retailEvent.EndDate),
                        retailEvent.Note,
                        covered_by = coveredBy,
                        no_promotion_yet = coveredBy.Count == 0,
                    };
                }),
        };
    }
}
