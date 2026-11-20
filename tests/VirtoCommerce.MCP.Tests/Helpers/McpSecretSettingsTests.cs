using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Settings;
using VirtoCommerce.Platform.Core.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpSecretSettingsTests
{
    private static readonly McpSecretSettings _rule = new(Options.Create(new McpSecretSettingsOptions()));

    [Theory]
    [InlineData("Stripe.ApiKey")]
    [InlineData("Stripe.SecretKey")]
    [InlineData("Smtp.Password")]
    [InlineData("Sanity.ApiToken")]
    [InlineData("Azure.ClientCredential")]
    [InlineData("Azure.Credentials")]
    [InlineData("GDPR.GDPRPassword")]
    [InlineData("Legacy.API_KEY")]
    [InlineData("Stripe.ApiKeys")]
    [InlineData("Webhook.Secrets")]
    [InlineData("Sanity.AccessTokens")]
    [InlineData("Smtp.Passwords")]
    [InlineData("Legacy.API_KEYS")]
    [InlineData("Stripe.ApiKey2")]
    [InlineData("Smtp.Password12")]
    public void IsSecret_WhenAWordOfTheNameIsASecretWord(string name)
    {
        Assert.True(_rule.IsSecret(name, SettingValueType.ShortText));
    }

    [Theory]
    [InlineData("Search.Keyword")]
    [InlineData("Seo.Tokenizer")]
    [InlineData("Catalog.Monkey")]
    [InlineData("Office.Secretary")]
    [InlineData("Stores.TaxCalculationEnabled")]
    [InlineData("Search.Keywords")]
    [InlineData("Seo.Tokenizers")]
    [InlineData("Office.Secretaries")]
    [InlineData("Catalog.Turkeys")]
    [InlineData(null)]
    public void IsNotSecret_WhenNoWholeWordIsASecretWord(string name)
    {
        Assert.False(_rule.IsSecret(name, SettingValueType.ShortText));
    }

    [Fact]
    public void IsSecret_WhenTypedSecureString_WhateverTheName()
    {
        Assert.True(_rule.IsSecret("Erp.SigningCode", SettingValueType.SecureString));
    }

    [Fact]
    public void IsSecret_ForEverySettingKnownToBeASecret_WhateverTheCase()
    {
        // In capitals, the names have no secret word of their own: only the list finds them, with no module registering it.
        Assert.All(McpKnownSecretSettings.SettingNames, name => Assert.True(_rule.IsSecret(name.ToUpperInvariant(), SettingValueType.ShortText)));
    }

    [Fact]
    public void IsSecret_WhenAModuleNamesIt_WhateverTheCase()
    {
        var options = new McpSecretSettingsOptions();
        options.SettingNames.Add("Erp.SigningCode");

        Assert.True(new McpSecretSettings(Options.Create(options)).IsSecret("erp.signingcode", SettingValueType.ShortText));
    }

    // The MCP module and Store both add the rules; a solution's own rule, registered after them, is the one every toolset
    // gets.
    [Fact]
    public void AddMcpSettingRules_AddsTheRulesOnce_AndASolutionsOwnRuleWins()
    {
        // Arrange
        var services = new ServiceCollection().AddOptions();
        services.AddMcpSettingRules().AddMcpSettingRules();
        services.AddSingleton<IMcpSecretSettings, NothingIsSecret>();

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.Equal(2, services.Count(x => x.ServiceType == typeof(IMcpSecretSettings)));
        Assert.Single(services, x => x.ServiceType == typeof(IMcpSettingTexts));
        Assert.IsType<NothingIsSecret>(provider.GetRequiredService<IMcpSecretSettings>());
    }

    private sealed class NothingIsSecret : IMcpSecretSettings
    {
        public bool IsSecret(string name, SettingValueType valueType) => false;
    }
}
