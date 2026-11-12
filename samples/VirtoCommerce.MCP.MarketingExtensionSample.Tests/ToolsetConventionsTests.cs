using VirtoCommerce.MCP.Testing;
using Xunit;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Tests;

// The marketing sample and the Contoso extension against the MCP toolset conventions. Each exception says why it is
// deliberate, where it is made.
public class ToolsetConventionsTests
{
    [Fact]
    public void MarketingSample_FollowsTheConventions()
    {
        McpToolsetConventions.Verify<MarketingSample.Module>(x => x
            .Ignore(McpConventionRules.Safeguard, "deactivate_stale_coupons", "Deactivates codes of promotions that have ended: it lowers risk, and its dry run lists the codes first.")
            .Ignore(McpConventionRules.Safeguard, "unpublish_content", "Takes a publication down at once: it lowers risk, as pausing a promotion does.")
            .Ignore(McpConventionRules.Paging, "search_promotions", "The sample's finders return the first page with the total; a real module adds skip with McpPage<T>.")
            .Ignore(McpConventionRules.Paging, "search_coupons", "As search_promotions.")
            .Ignore(McpConventionRules.Paging, "search_content_items", "As search_promotions.")
            .Ignore(McpConventionRules.Paging, "list_content_placeholders", "A store has a handful of placeholders, one per slot of a page.")
            .Ignore(McpConventionRules.Paging, "list_customer_segments", "The sample has five segments."));
    }

    [Fact]
    public void ContosoExtension_FollowsTheConventions()
    {
        McpToolsetConventions.Verify<Module>();
    }
}
