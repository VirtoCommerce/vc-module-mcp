using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using VirtoCommerce.MCP.Core.Models;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpSetupValuesTests
{
    // A change of letter case is a change of a text, as the platform compares setting values.
    [Theory]
    [InlineData(10, 10.0, true)]
    [InlineData("10", 10, true)]
    [InlineData("10.50", 10.5, true)]
    [InlineData(10, 11, false)]
    [InlineData("Abc", "Abc", true)]
    [InlineData("Abc", "aBC", false)]
    [InlineData("abc", "abd", false)]
    [InlineData(null, "", true)]
    [InlineData(null, "x", false)]
    [InlineData(true, "True", true)]
    [InlineData("true", "True", true)]
    [InlineData(false, "true", false)]
    public void AreEqual_ComparesNumbersAndBooleansByValue_AndTextExactly(object left, object right, bool expected)
    {
        Assert.Equal(expected, McpSetupValues.AreEqual(left, right));
        Assert.Equal(expected, McpSetupValues.AreEqual(right, left));
    }

    // How an allowed value sent in another case is found.
    [Fact]
    public void AreEqual_IgnoresTheCaseOfTexts_WhenAsked()
    {
        Assert.True(McpSetupValues.AreEqual("Live", "live", StringComparison.OrdinalIgnoreCase));
        Assert.False(McpSetupValues.AreEqual("Live", "Test", StringComparison.OrdinalIgnoreCase));
        Assert.True(McpSetupValues.AreEqual(10, "10.0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AreEqual_ReadsTheValueOfAJsonElementFromARequest()
    {
        Assert.True(McpSetupValues.AreEqual(Json("\"Abc\""), "Abc"));
        Assert.False(McpSetupValues.AreEqual(Json("\"Abc\""), "abc"));
        Assert.True(McpSetupValues.AreEqual(Json("5"), 5.0));
        Assert.True(McpSetupValues.AreEqual(Json("true"), true));
        Assert.True(McpSetupValues.AreEqual(Json("null"), null));
        Assert.False(McpSetupValues.AreEqual(Json("\"Abc\""), "\"Abc\""));
    }

    [Fact]
    public void ToInvariantString_IgnoresTheCurrentCulture()
    {
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("1.5", McpSetupValues.ToInvariantString(1.5));
            Assert.Equal("2026-11-18T10:00:00.0000000Z", McpSetupValues.ToInvariantString(new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}

[Trait("Category", "Unit")]
public class McpSetupPartPlanTests
{
    [Fact]
    public void AddChange_RecordsTheFieldWithBothValues()
    {
        var plan = new McpSetupPartPlan();

        var returned = plan.AddChange("basics.name", "Old name", "New name").AddChange("basics.url", null, "https://shop.example.com");

        Assert.Same(plan, returned);
        Assert.Collection(plan.Changes,
            x =>
            {
                Assert.Equal("basics.name", x.Field);
                Assert.Equal("Old name", x.From);
                Assert.Equal("New name", x.To);
            },
            x =>
            {
                Assert.Equal("basics.url", x.Field);
                Assert.Null(x.From);
                Assert.Equal("https://shop.example.com", x.To);
            });
    }

    // Two equal values are no change, as in McpChangePreview; a new list is a change whatever its items.
    [Fact]
    public void AddChange_SkipsTwoEqualValues_ButRecordsANewList()
    {
        var plan = new McpSetupPartPlan();

        plan.AddChange("methods.cash.active", true, true)
            .AddChange("methods.cash.priority", 10m, 10.0m)
            .AddChange("basics.url", null, null)
            .AddChange("basics.currencies", new List<string> { "EUR" }, new List<string> { "EUR", "USD" });

        Assert.Equal(["basics.currencies"], plan.Changes.Select(x => x.Field));
    }
}
