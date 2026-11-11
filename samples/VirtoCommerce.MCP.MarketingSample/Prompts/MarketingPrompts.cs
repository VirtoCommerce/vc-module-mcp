using System.ComponentModel;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;

namespace VirtoCommerce.MCP.MarketingSample.Prompts;

// Prompts are the marketer's routines, offered by MCP clients as slash commands, for example /weekly_marketing_review
// in Claude. They only describe what to do; the tools decide what the user may see and change.
[McpServerPromptType]
[McpScope(McpScopes.Admin)]
public static class MarketingPrompts
{
    [McpServerPrompt(Name = "weekly_marketing_review")]
    [Description("Monday review of promotions: what runs, what needs attention, and a short action list.")]
    public static string WeeklyMarketingReview(
        [Description("Store id. Leave empty for every store you manage.")] string store_id = null)
    {
        return $"""
            Run my weekly marketing review{StoreClause(store_id)}.

            1. Call get_marketing_overview{ArgumentClause(store_id)}.
            2. Call find_promotion_issues{ArgumentClause(store_id)} and find_promotion_conflicts{ArgumentClause(store_id)}.
            3. Call analyze_promotion_performance{ArgumentClause(store_id)} with last_days 7.

            Then write the review for me:
            - Three numbers first: discount spend this month against the budget, active promotions, open high-severity issues.
            - What works: the best two promotions of the week, with revenue per unit of discount.
            - What needs action: at most five items, most urgent first. For each, the promotion, the problem in one sentence,
              and the action you propose with the tool you would use.
            - Upcoming: retail events in the next 30 days and whether a promotion covers them.

            Do not change anything. Ask me which actions to take; changes that need approval are submitted for approval.
            """;
    }

    [McpServerPrompt(Name = "plan_campaign")]
    [Description("Plan a campaign for a retail event, such as Black Friday, and prepare the promotions as drafts.")]
    public static string PlanCampaign(
        [Description("The event or the goal, for example 'Black Friday' or 'clear winter boots stock'.")] string goal,
        [Description("Store id.")] string store_id,
        [Description("Discount budget in the store currency.")] string budget = null)
    {
        return $"""
            Help me plan a campaign for "{goal}" in store {store_id}{(string.IsNullOrEmpty(budget) ? "" : $" with a discount budget of {budget}")}.

            1. Read the discount policy resource (marketing://policies/discounts) and the playbook for big sale events
               (marketing://playbooks/sale-event).
            2. Call get_promotion_calendar for the store, covering the event, and search_promotions for promotions of the
               same campaign, so we do not duplicate what exists.
            3. Call analyze_promotion_performance for the store to see which offers worked best for the money.
            4. Propose two or three promotions: offer, target categories, dates, audience, whether a coupon is needed, and
               the expected discount cost. Keep within the policy and the budget.

            When I agree, create them with create_promotion_draft, add the customer-facing name for every store language,
            and run find_promotion_conflicts. Show me the drafts and the remaining warnings. Do not schedule them until I ask:
            schedule_promotion needs approval.
            """;
    }

    [McpServerPrompt(Name = "promotion_post_mortem")]
    [Description("What a finished or running promotion achieved, and what to do differently next time.")]
    public static string PromotionPostMortem(
        [Description("Promotion id.")] string promotion_id)
    {
        return $"""
            Write a short post-mortem of promotion {promotion_id}.

            Call get_promotion and analyze_promotion_performance for it, and analyze_promotion_performance for its store to
            compare it with the other promotions.

            Cover: the goal as the offer suggests it, the results (redemptions, revenue, discount cost, revenue per unit of
            discount, order value against the store average, budget used), how it ranks in the store, what went well, what
            did not, and three concrete recommendations for the next promotion of this kind. Use numbers from the tools only;
            say so when a number is an estimate.
            """;
    }

    private static string StoreClause(string storeId)
    {
        return string.IsNullOrEmpty(storeId) ? " for all my stores" : $" for store {storeId}";
    }

    private static string ArgumentClause(string storeId)
    {
        return string.IsNullOrEmpty(storeId) ? string.Empty : $" with store_id {storeId}";
    }
}
