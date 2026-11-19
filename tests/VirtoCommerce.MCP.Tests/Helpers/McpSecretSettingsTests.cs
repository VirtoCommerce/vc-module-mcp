using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Settings;
using VirtoCommerce.Platform.Core.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpSecretSettingsTests
{
    private static readonly McpSecretSettingsOptions _noNamedSecrets = new();

    [Theory]
    [InlineData("Stripe.ApiKey")]
    [InlineData("Stripe.SecretKey")]
    [InlineData("Smtp.Password")]
    [InlineData("Sanity.ApiToken")]
    [InlineData("Azure.ClientCredential")]
    [InlineData("Azure.Credentials")]
    [InlineData("GDPR.GDPRPassword")]
    [InlineData("Legacy.API_KEY")]
    public void IsSecret_WhenAWordOfTheNameIsASecretWord(string name)
    {
        Assert.True(McpSecretSettings.IsSecret(name, SettingValueType.ShortText, _noNamedSecrets));
    }

    [Theory]
    [InlineData("Search.Keyword")]
    [InlineData("Seo.Tokenizer")]
    [InlineData("Catalog.Monkey")]
    [InlineData("Office.Secretary")]
    [InlineData("Stores.TaxCalculationEnabled")]
    [InlineData(null)]
    public void IsNotSecret_WhenNoWholeWordIsASecretWord(string name)
    {
        Assert.False(McpSecretSettings.IsSecret(name, SettingValueType.ShortText, _noNamedSecrets));
    }

    [Fact]
    public void IsSecret_WhenTypedSecureString_WhateverTheName()
    {
        Assert.True(McpSecretSettings.IsSecret("Erp.SigningCode", SettingValueType.SecureString, _noNamedSecrets));
    }

    [Fact]
    public void IsSecret_ForEverySettingKnownToBeASecret_WhateverTheCase()
    {
        // In capitals, the names have no secret word of their own: only the list finds them, with no module registering it.
        Assert.All(McpKnownSecretSettings.SettingNames, name => Assert.True(McpSecretSettings.IsSecret(name.ToUpperInvariant(), SettingValueType.ShortText, _noNamedSecrets)));
    }

    [Fact]
    public void IsSecret_WhenAModuleNamesIt_WhateverTheCase()
    {
        var options = new McpSecretSettingsOptions();
        options.SettingNames.Add("Erp.SigningCode");

        Assert.True(McpSecretSettings.IsSecret("erp.signingcode", SettingValueType.ShortText, options));
    }
}
