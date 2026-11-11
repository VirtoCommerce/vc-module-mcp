using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.MarketingSample.Data;

namespace VirtoCommerce.MCP.MarketingSample.Resources;

// Reference material the agent reads before it plans: the company's rules and the retail calendar. Resources hold
// knowledge that changes rarely, so tools stay about data and actions.
[McpServerResourceType]
[McpScope(McpScopes.Admin)]
public class MarketingResources(MarketingDemoStore store)
{
    private const int CalendarMonths = 12;

    private const string SaleEventPlaybookText = """
        # Sale event playbook

        ## Timeline
        - 4 weeks before: offers agreed, drafts created, budgets set.
        - 3 weeks before: translations done, conflicts checked, coupons generated for code-only offers.
        - 2 weeks before: promotions scheduled and approved.
        - Event week: check the overview every morning; pause anything over budget.
        - 1 week after: deactivate stale coupons, write the post-mortem.

        ## Offers that worked
        - Category doorbusters (20% on laptops, monitors, audio) bring the most revenue per unit of discount.
        - Cyber Monday works best as a code: a fixed amount off above a minimum order.
        - Free shipping thresholds lift the order value; do not run two free shipping offers for the same customers.

        ## Before scheduling
        - find_promotion_conflicts shows no stacking above the policy limit.
        - Every store language has a customer-facing name.
        - The discount budget covers the expected volume: about 3x a normal week.
        """;

    [McpServerResource(UriTemplate = "marketing://policies/discounts", Name = "discount_policy", MimeType = "text/markdown")]
    [Description("The company's discount policy: limits, approvals, exclusivity, coupons and translations.")]
    public static string DiscountPolicy()
    {
        return $"""
            # Discount policy

            ## Limits
            - A percent discount is at most {MarketingSampleConstants.MaxPercentWithoutSignOff}%. Above that, up to {MarketingSampleConstants.MaxPercent}%, the marketing director signs off first.
            - Discounts that stack on the same items must stay within {MarketingSampleConstants.MaxPercentWithoutSignOff}% together, or one of them is exclusive.
            - A percent discount for everyone has an end date. Evergreen offers are for a segment (new customers, VIP) or need a minimum order.

            ## Budgets
            - Every promotion with a percent or amount discount has a discount budget.
            - At {MarketingSampleConstants.BudgetWarningShare:P0} of the budget, the owner decides: pause, lower the discount, or ask for more budget.
            - Each store has a monthly discount budget; the overview shows the spend against it.

            ## Approvals
            - Going live, extending and changing a discount are approved by a second person (the mcp:approve permission).
            - Pausing does not need approval: stop first, discuss after.
            - Drafts and translations do not need approval.

            ## Exclusivity and priority
            - Exclusive promotions have priority 50 or higher.
            - Two exclusive promotions never share a priority in the same period.

            ## Coupons
            - A promotion that needs a code has its coupons before it starts.
            - Single-use codes for partners and newsletters; one public multi-use code at most per promotion.
            - Coupons of ended promotions are deactivated within a week.

            ## Content
            - Every promotion has a customer-facing name in every language of its store before it goes live.
            """;
    }

    [McpServerResource(UriTemplate = "marketing://playbooks/sale-event", Name = "sale_event_playbook", MimeType = "text/markdown")]
    [Description("Checklist for big sale events such as Black Friday and Cyber Monday: timeline, offers that work, and what to check.")]
    public static string SaleEventPlaybook()
    {
        return SaleEventPlaybookText;
    }

    [McpServerResource(UriTemplate = "marketing://calendar/retail-events", Name = "retail_calendar", MimeType = "text/markdown")]
    [Description("Retail events of the next 12 months with their dates.")]
    public string RetailCalendarResource()
    {
        var now = store.Now;
        var builder = new StringBuilder("# Retail calendar\n\n| Event | Start | End | Note |\n| --- | --- | --- | --- |\n");

        foreach (var retailEvent in RetailCalendar.GetEvents(now.Date, now.Date.AddMonths(CalendarMonths)).Where(x => x.EndDate >= now.Date))
        {
            builder.Append($"| {retailEvent.Name} | {retailEvent.StartDate:yyyy-MM-dd} | {retailEvent.EndDate:yyyy-MM-dd} | {retailEvent.Note} |\n");
        }

        return builder.ToString();
    }
}
