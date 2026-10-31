using System;
using System.Collections.Generic;
using System.Text.Json;
using VirtoCommerce.MCP.MarketingSample.Tools;
using Xunit;

namespace VirtoCommerce.MCP.MarketingExtensionSample.Tests;

// The conditions that decide whether a call of the marketing sample needs approval, which the extension relies on.
[Trait("Category", "Unit")]
public class ApprovalConditionTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    public void GenerateCoupons_Should_NeedApproval_Only_AboveTheThreshold(int count, bool expected)
    {
        // Act & Assert
        Assert.Equal(expected, CouponBulkTools.NeedsApproval(Arguments(new { count })));
    }

    [Fact]
    public void GenerateCoupons_Should_NeedApproval_When_TheCountIsMissing()
    {
        // Act & Assert
        Assert.True(CouponBulkTools.NeedsApproval(Arguments(new { })));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(3, false)]
    public void PublishContent_Should_NeedApproval_Only_When_ItGoesLiveToday(int startInDays, bool expected)
    {
        // Arrange
        var start = DateTime.UtcNow.Date.AddDays(startInDays).ToString("yyyy-MM-dd");

        // Act & Assert
        Assert.Equal(expected, ContentTools.GoesLiveToday(Arguments(new { start_date = start })));
    }

    private static IReadOnlyDictionary<string, JsonElement> Arguments(object value)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value));
    }
}
