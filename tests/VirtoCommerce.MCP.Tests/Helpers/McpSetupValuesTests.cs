using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpSetupValuesTests
{
    private static readonly McpSetupValues _values = new();

    // A number or a boolean on either side compares by value; two texts compare as texts, so a change of letter case or
    // of leading zeros is a change, as the platform compares setting values.
    [Theory]
    [InlineData(10, 10.0, true)]
    [InlineData("10", 10, true)]
    [InlineData("10.50", 10.5, true)]
    [InlineData("007", 7, true)]
    [InlineData(10, 11, false)]
    [InlineData("007", "7", false)]
    [InlineData("1.10", "1.1", false)]
    [InlineData("Abc", "Abc", true)]
    [InlineData("Abc", "aBC", false)]
    [InlineData("abc", "abd", false)]
    [InlineData(null, "", true)]
    [InlineData(null, "x", false)]
    [InlineData(null, 0, false)]
    [InlineData(true, "True", true)]
    [InlineData("true", "True", false)]
    [InlineData(false, "true", false)]
    public void AreEqual_ComparesNumbersAndBooleansByValue_AndTwoTextsExactly(object left, object right, bool expected)
    {
        Assert.Equal(expected, _values.AreEqual(left, right));
        Assert.Equal(expected, _values.AreEqual(right, left));
    }

    // How an allowed value sent in another case is found.
    [Fact]
    public void AreEqual_IgnoresTheCaseOfTexts_WhenAsked()
    {
        Assert.True(_values.AreEqual("Live", "live", StringComparison.OrdinalIgnoreCase));
        Assert.False(_values.AreEqual("Live", "Test", StringComparison.OrdinalIgnoreCase));
        Assert.True(_values.AreEqual(10, "10.0", StringComparison.OrdinalIgnoreCase));
        Assert.True(_values.AreEqual("true", "True", StringComparison.OrdinalIgnoreCase));
        Assert.False(_values.AreEqual("007", "7", StringComparison.OrdinalIgnoreCase));
    }

    // A JSON number or boolean from a request is a value; a JSON string is a text.
    [Fact]
    public void AreEqual_ReadsTheValueOfAJsonElementFromARequest()
    {
        Assert.True(_values.AreEqual(Json("\"Abc\""), "Abc"));
        Assert.False(_values.AreEqual(Json("\"Abc\""), "abc"));
        Assert.True(_values.AreEqual(Json("5"), 5.0));
        Assert.True(_values.AreEqual(Json("7"), "007"));
        Assert.False(_values.AreEqual(Json("\"7\""), "007"));
        Assert.True(_values.AreEqual(Json("true"), true));
        Assert.True(_values.AreEqual(Json("true"), "True"));
        Assert.False(_values.AreEqual(Json("\"true\""), "True"));
        Assert.True(_values.AreEqual(Json("null"), null));
        Assert.False(_values.AreEqual(Json("\"Abc\""), "\"Abc\""));
    }

    [Fact]
    public void ToInvariantString_IgnoresTheCurrentCulture()
    {
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("1.5", _values.ToInvariantString(1.5));
            Assert.Equal("2026-11-18T10:00:00.0000000Z", _values.ToInvariantString(new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // The MCP module and Store both add the rules; a solution's own comparison, registered after them, is the one every
    // setup gets.
    [Fact]
    public void AddMcpSettingRules_AddsTheComparison_AndASolutionsOwnWins()
    {
        var services = new ServiceCollection();

        services.AddMcpSettingRules().AddMcpSettingRules();
        Assert.IsType<McpSetupValues>(services.BuildServiceProvider().GetRequiredService<IMcpSetupValues>());

        services.AddSingleton<IMcpSetupValues, TextsOnly>();
        Assert.IsType<TextsOnly>(services.BuildServiceProvider().GetRequiredService<IMcpSetupValues>());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private sealed class TextsOnly : McpSetupValues
    {
        protected override bool IsNumber(object value)
        {
            return false;
        }
    }
}
