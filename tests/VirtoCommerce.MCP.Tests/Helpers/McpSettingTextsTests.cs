using Newtonsoft.Json.Linq;
using VirtoCommerce.MCP.Core.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpSettingTextsTests
{
    [Fact]
    public void Find_FollowsTheDotsOfTheName()
    {
        var translation = JObject.Parse("""{ "settings": { "Stores": { "TaxCalculationEnabled": { "title": "Tax calculation", "description": "Calculates taxes." } } } }""");

        Assert.Equal(("Tax calculation", "Calculates taxes."), McpSettingTexts.Find(translation, "Stores.TaxCalculationEnabled"));
    }

    [Fact]
    public void Find_TriesTheLongestKeyFirst()
    {
        var translation = JObject.Parse("""
            { "settings": {
                "Shipping.Bopis.Enabled": { "title": "Whole name" },
                "Shipping": { "Bopis": { "Enabled": { "title": "Nested" } } } } }
            """);

        Assert.Equal("Whole name", McpSettingTexts.Find(translation, "Shipping.Bopis.Enabled").Title);
    }

    [Fact]
    public void Find_TriesShorterKeys_WhenALongerOneLeadsNowhere()
    {
        var translation = JObject.Parse("""
            { "settings": {
                "Shipping.Bopis": { "Other": { "title": "Another setting" } },
                "Shipping": { "Bopis": { "Enabled": { "title": "Pickup" } } } } }
            """);

        Assert.Equal("Pickup", McpSettingTexts.Find(translation, "Shipping.Bopis.Enabled").Title);
    }

    [Fact]
    public void Find_GivesTheDisplayNameOrTheName_ForATitleTheFilesDoNotHave()
    {
        var translation = JObject.Parse("""{ "settings": { "Stores": { "Blank": { "title": " " } } } }""");

        Assert.Equal(("Blank setting", null), McpSettingTexts.Find(translation, "Stores.Blank", "Blank setting"));
        Assert.Equal(("Stores.Blank", null), McpSettingTexts.Find(translation, "Stores.Blank"));
        Assert.Equal(("Stores.Unknown", null), McpSettingTexts.Find(translation, "Stores.Unknown"));
        Assert.Equal(("Stores.Blank", null), McpSettingTexts.Find(translation: null, "Stores.Blank"));
    }
}
