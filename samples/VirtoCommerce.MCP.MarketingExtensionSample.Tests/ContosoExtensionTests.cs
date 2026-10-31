using System.Linq;
using ModelContextProtocol;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.MarketingExtensionSample.Models;
using VirtoCommerce.MCP.MarketingExtensionSample.Services;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;
using Xunit;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Tests;

[Trait("Category", "Unit")]
public class ContosoExtensionTests
{
    private readonly ContosoTestContext _context = new();

    [Fact]
    public void Store_Should_CreateTheExtendedPromotion_For_EverySeededPromotion()
    {
        // Act
        var promotions = _context.Store.GetPromotions();

        // Assert
        Assert.NotEmpty(promotions);
        Assert.All(promotions, promotion => Assert.IsType<ContosoPromotion>(promotion));
    }

    [Fact]
    public void Clone_Should_KeepTheExtendedField()
    {
        // Arrange
        var promotion = (ContosoPromotion)_context.Store.GetPromotion("black-friday-doorbusters");
        promotion.CostCenter = "CC-4100";

        // Act
        var clone = promotion.Clone();

        // Assert
        Assert.Equal("CC-4100", Assert.IsType<ContosoPromotion>(clone).CostCenter);
    }

    [Fact]
    public void CheckPolicy_Should_AddTheCompanyRules_To_TheSampleRules()
    {
        // Arrange: 20% off laptops with a budget of 120,000 and no cost center
        var promotion = _context.Store.GetPromotion("black-friday-doorbusters");

        // Act
        var warnings = _context.Analytics.CheckPolicy(promotion);

        // Assert
        Assert.Contains(warnings, x => x.StartsWith("Contoso finance:"));
        Assert.Contains(warnings, x => x.StartsWith("Contoso margin floor:") && x.Contains("Laptops"));
        Assert.Contains(warnings, x => x.Contains("fr-FR"));
    }

    [Fact]
    public void FindIssues_Should_ReportPromotionsBelowTheMarginFloor_And_WithoutACostCenter()
    {
        // Act
        var issues = _context.Analytics.FindIssues(_context.Store.GetPromotions());

        // Assert
        Assert.Contains(issues, x => x.Code == ContosoMarketingAnalytics.BelowMarginFloorCode && x.PromotionId == "autumn-laptops" && x.Severity == "high");
        Assert.Contains(issues, x => x.Code == ContosoMarketingAnalytics.MissingCostCenterCode && x.PromotionId == "black-friday-doorbusters");
        Assert.Contains(issues, x => x.Code == "policy_limit_exceeded" && x.PromotionId == "headphones-flash");
    }

    [Fact]
    public void SetCostCenter_Should_ShowTheChange_Without_SavingIt_On_ADryRun()
    {
        // Act
        var preview = _context.Tools.SetCostCenter(ContosoTestContext.Marketer(), "black-friday-doorbusters", "cc-4100", dryRun: true);

        // Assert
        var change = Assert.Single(preview.Changes);
        Assert.Equal("CC-4100", change.To);
        Assert.DoesNotContain(preview.Warnings, x => x.StartsWith("Contoso finance:"));
        Assert.Null(((ContosoPromotion)_context.Store.GetPromotion("black-friday-doorbusters")).CostCenter);
    }

    [Fact]
    public void SetCostCenter_Should_SaveTheCostCenter_And_ClearTheIssue()
    {
        // Act
        _context.Tools.SetCostCenter(ContosoTestContext.Marketer(), "black-friday-doorbusters", "CC-4100", dryRun: false);

        // Assert
        var promotion = (ContosoPromotion)_context.Store.GetPromotion("black-friday-doorbusters");
        Assert.Equal("CC-4100", promotion.CostCenter);
        Assert.DoesNotContain(_context.Analytics.FindIssues([promotion]), x => x.Code == ContosoMarketingAnalytics.MissingCostCenterCode);
    }

    [Theory]
    [InlineData("4100")]
    [InlineData("CC-41")]
    [InlineData("")]
    public void SetCostCenter_Should_RefuseAnInvalidCostCenter(string costCenter)
    {
        // Act & Assert
        Assert.Throws<McpException>(() => _context.Tools.SetCostCenter(ContosoTestContext.Marketer(), "black-friday-doorbusters", costCenter, dryRun: true));
    }

    [Fact]
    public void SetCostCenter_Should_RefuseAPromotionOfAStoreTheUserDoesNotManage()
    {
        // Act & Assert
        Assert.Throws<McpForbiddenException>(() => _context.Tools.SetCostCenter(ContosoTestContext.FashionMarketer(), "black-friday-doorbusters", "CC-4100", dryRun: true));
    }

    [Fact]
    public void Explain_Should_TellWhyTheWelcomeCodeDidNotApply_To_TheComplaintOrder()
    {
        // Arrange
        var evaluator = new PromotionEvaluator(_context.Store);
        var order = _context.Store.GetOrder(MarketingDemoOrders.ComplaintOrderNumber);

        // Act
        var welcome = evaluator.Explain(order).Single(x => x.Promotion.Id == "welcome-10");

        // Assert
        Assert.Equal("c-anna-k", order.CustomerId);
        Assert.False(welcome.Applied);
        Assert.Contains("ordered before", welcome.Reason);
    }

    [Fact]
    public void GetMarginFloorBreaches_Should_IgnoreCategoriesWithoutAFloor()
    {
        // Arrange: 20% off outerwear; Contoso has no floor for it
        var promotion = _context.Store.GetPromotion("fashion-winter-coats");

        // Act & Assert
        Assert.Empty(_context.Analytics.GetMarginFloorBreaches(promotion));
    }

    [Fact]
    public void Options_Should_LowerTheThreshold_Of_TheCostCenterRule()
    {
        // Arrange: the fashion coats promotion has a budget of 15,000
        _context.Options.Value.CostCenterBudgetThreshold = 10_000m;
        var promotion = _context.Store.GetPromotion("fashion-winter-coats");

        // Act & Assert
        Assert.True(_context.Analytics.NeedsCostCenter(promotion));
        Assert.Contains(_context.Store.GetPromotions(), x => x.Id == promotion.Id && x.StoreId == MarketingDemoSeed.FashionStoreId);
    }
}
