using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Testing;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.MCP.Web.Tools;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Localizations;
using VirtoCommerce.Platform.Core.Settings;
using VirtoCommerce.Platform.Data.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Tools;

// The settings tools over the platform's own Settings V2 service, which reads a faked settings manager: what the back
// office shows, the platform-wide values, and the settings forced by the configuration come from the platform's code.
[Trait("Category", "Unit")]
public sealed class McpSettingsToolsTests : IAsyncDisposable
{
    private const string Origin = "https://admin.example.com";

    private static readonly SettingDescriptor _taxCalculation = Setting("Stores.TaxCalculationEnabled", "VirtoCommerce.Store", "Stores|General", SettingValueType.Boolean, true);
    private static readonly SettingDescriptor _fullTextSearch = Setting("Catalog.Search.UseFullText", "VirtoCommerce.Catalog", "Catalog|Search", SettingValueType.Boolean, false);
    private static readonly SettingDescriptor _senderEmail = Setting("Notifications.SenderEmail", "VirtoCommerce.Notifications", "Notifications|Email", SettingValueType.ShortText, "noreply@example.com");
    private static readonly SettingDescriptor _keywordLength = Setting("Search.Keyword.MinLength", "VirtoCommerce.Search", "Search|General", SettingValueType.PositiveInteger, 3);

    private static readonly SettingDescriptor _imageCategories = new()
    {
        Name = "Catalog.ImageCategories", ModuleId = "VirtoCommerce.Catalog", GroupName = "Catalog|General", ValueType = SettingValueType.ShortText,
        IsDictionary = true, AllowedValues = ["Images", "Thumbnails"],
    };

    private static readonly SettingDescriptor _orderStatus = new()
    {
        Name = "Orders.DefaultStatus", ModuleId = "VirtoCommerce.Orders", GroupName = "Orders|General", ValueType = SettingValueType.ShortText,
        DefaultValue = "New", AllowedValues = ["New", "Processing"],
    };

    private static readonly SettingDescriptor _cacheExpiration = new()
    {
        Name = "Cache.AbsoluteExpiration", ModuleId = "VirtoCommerce.Platform", GroupName = "Platform|Cache", ValueType = SettingValueType.Integer,
        DefaultValue = 60, RestartRequired = true,
    };

    // Secrets: by a word of the name, by the type, and by a name a module declared.
    private static readonly SettingDescriptor _stripeKey = Setting("Payment.Stripe.ApiKey", "VirtoCommerce.Payment", "Payment|Stripe", SettingValueType.ShortText, "sk_test_0000");
    private static readonly SettingDescriptor _smtpSignature = Setting("Notifications.Smtp.Signature", "VirtoCommerce.Notifications", "Notifications|Email", SettingValueType.SecureString, null);
    private static readonly SettingDescriptor _erpSigningCode = Setting("Erp.SigningCode", "Partner.Erp", "ERP|Connection", SettingValueType.ShortText, null);

    private static readonly SettingDescriptor _hidden = new()
    {
        Name = "Platform.Internal.Marker", ModuleId = "VirtoCommerce.Platform", GroupName = "Platform|Internal", ValueType = SettingValueType.ShortText,
        IsHidden = true,
    };

    private static readonly SettingDescriptor[] _descriptors =
    [
        _taxCalculation, _fullTextSearch, _imageCategories, _orderStatus, _senderEmail, _cacheExpiration, _keywordLength,
        _stripeKey, _smtpSignature, _erpSigningCode, _hidden,
    ];

    // The values saved for the platform; a dictionary setting saves its items.
    private static readonly Dictionary<string, object> _saved = new()
    {
        [_fullTextSearch.Name] = true,
        [_imageCategories.Name] = new object[] { "Images", "Thumbnails", "Banners" },
        [_orderStatus.Name] = "Processing",
        [_stripeKey.Name] = "sk_live_4242",
        [_smtpSignature.Name] = "smtp-signature-0042",
        [_erpSigningCode.Name] = "erp-signing-0042",
        [_hidden.Name] = "hidden-0042",
    };

    private const string Translations = """
        {
          "settings": {
            "Stores": { "TaxCalculationEnabled": { "title": "Calculate sales tax", "description": "Adds tax to the orders of a store." } },
            "Payment.Stripe": { "ApiKey": { "title": "Stripe API key", "description": "The secret key of the Stripe account." } }
          }
        }
        """;

    private static readonly McpTestUser _settingsReader = McpTestUser.Create("reader").WithPermissions(PlatformConstants.Security.Permissions.SettingQuery);

    private readonly Mock<ISettingsManager> _settingsManager = new();
    private readonly Mock<ITranslationService> _translationService = new();
    private readonly SettingsPropertyService _settingsPropertyService;
    private readonly McpToolHarness _harness;

    public McpSettingsToolsTests()
    {
        _settingsManager.Setup(x => x.AllRegisteredSettings).Returns(_descriptors);
        _settingsManager.Setup(x => x.GetSettingTypeAssignments()).Returns(new Dictionary<string, string[]> { [_taxCalculation.Name] = ["Store"] });
        _settingsManager
            .Setup(x => x.GetObjectSettingsAsync(It.IsAny<IEnumerable<string>>(), null, null))
            .ReturnsAsync((IEnumerable<string> names, string _, string _) => names.Select(GetSavedSetting).ToList().AsEnumerable());

        _translationService.Setup(x => x.GetTranslationDataForLanguage("en")).Returns(JObject.Parse(Translations));
        _settingsPropertyService = new SettingsPropertyService(_settingsManager.Object, new FixedOverrides(_senderEmail.Name));

        var pageSize = new Mock<IMcpPageSizeResolver>();
        pageSize.Setup(x => x.GetPageSizeAsync(It.IsAny<int?>())).ReturnsAsync((int? take) => Math.Clamp(take ?? 20, 1, 50));

        var originResolver = new Mock<IMcpPublicOriginResolver>();
        originResolver.Setup(x => x.GetOriginAsync(It.IsAny<HttpContext>(), It.IsAny<McpScope>())).ReturnsAsync(Origin);

        _harness = McpToolHarness.Create(
            services => services.AddMcpTools<McpSettingsTools>(),
            services => services
                .AddSingleton<ISettingsPropertyService>(_settingsPropertyService)
                .AddSingleton(_translationService.Object)
                .AddSingleton(pageSize.Object)
                .AddSingleton(originResolver.Object)
                .AddTransient<McpSettingsService>()
                .AddMcpSecretSettings(McpKnownSecretSettings.SettingNames)
                .AddMcpSecretSettings(_erpSigningCode.Name),
            scope: null);
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public void TheSettingsTools_AreVisibleWithThePermissionToReadSettingsOnly()
    {
        Assert.Equal(["explain_setting", "search_settings"], _harness.GetVisibleToolNames(_settingsReader));
        Assert.Empty(_harness.GetVisibleToolNames(McpTestUser.Create("store-admin").WithPermissions("store:read", "store:update")));
    }

    [Fact]
    public void TheSettingsTools_FollowTheConventions()
    {
        McpToolsetConventions.Verify([typeof(McpSettingsTools)]);
    }

    [Fact]
    public async Task SearchSettings_ListsWhatTheBackOfficeShows_WithoutSecretsOrTheirValues()
    {
        // Act
        var result = await CallAsync("search_settings", new { take = 50 });

        // Assert
        result.EnsureSucceeded();
        Assert.Equal(
            [_imageCategories.Name, _fullTextSearch.Name, _senderEmail.Name, _orderStatus.Name, _cacheExpiration.Name, _keywordLength.Name, _taxCalculation.Name],
            GetNames(result));
        Assert.All(new[] { "sk_live_4242", "sk_test_0000", "smtp-signature-0042", "erp-signing-0042", "hidden-0042" }, value => Assert.DoesNotContain(value, result.Text));
        Assert.Equal($"Secrets are not listed. A person enters them in the back office: Settings ({Origin}/#!/workspace/settings).",
            result.Json.GetProperty("secrets").GetString());
    }

    [Fact]
    public async Task SearchSettings_NeedsEveryWordOfTheKeyword_InTheNameTitleDescriptionOrGroup()
    {
        // "sales" is in the title only, "orders" in the description only.
        var found = await CallAsync("search_settings", new { keyword = "sales orders" });
        var notFound = await CallAsync("search_settings", new { keyword = "sales refunds" });

        Assert.Equal([_taxCalculation.Name], GetNames(found));
        Assert.Empty(GetNames(notFound));
    }

    [Fact]
    public async Task SearchSettings_FiltersByModuleWhateverItsCase_AndToTheChangedSettings()
    {
        // Act
        var catalog = await CallAsync("search_settings", new { module_id = "virtocommerce.catalog" });
        var changed = await CallAsync("search_settings", new { changed_only = true });

        // Assert
        Assert.Equal([_imageCategories.Name, _fullTextSearch.Name], GetNames(catalog));
        Assert.Equal([_imageCategories.Name, _fullTextSearch.Name, _orderStatus.Name], GetNames(changed));

        var fullTextSearch = GetRow(changed, _fullTextSearch.Name);
        Assert.True(fullTextSearch.GetProperty("value").GetBoolean());
        Assert.False(fullTextSearch.GetProperty("default_value").GetBoolean());
        Assert.True(fullTextSearch.GetProperty("is_changed").GetBoolean());
        Assert.Equal("Catalog > Search", fullTextSearch.GetProperty("group").GetString());
    }

    [Fact]
    public async Task SearchSettings_AModuleWithoutSettings_IsRefusedWithAHint()
    {
        var result = await CallAsync("search_settings", new { module_id = "VirtoCommerce.Catalgo" });

        Assert.Equal(McpToolCallOutcome.Failed, result.Outcome);
        Assert.Contains("Module 'VirtoCommerce.Catalgo' has no settings here", result.Error);
    }

    [Fact]
    public async Task SearchSettings_Pages_AndTwoPagesHoldEverySettingOnce()
    {
        // Act
        var first = await CallAsync("search_settings", new { take = 4 });
        var nextSkip = first.Json.GetProperty("settings").GetProperty("next_skip").GetInt32();
        var second = await CallAsync("search_settings", new { skip = nextSkip, take = 4 });

        // Assert
        Assert.Equal(7, first.Json.GetProperty("settings").GetProperty("total_count").GetInt32());
        Assert.True(first.Json.GetProperty("settings").GetProperty("has_more").GetBoolean());
        Assert.False(second.Json.GetProperty("settings").GetProperty("has_more").GetBoolean());

        var names = GetNames(first).Concat(GetNames(second)).ToList();
        Assert.Equal(7, names.Count);
        Assert.Equal(7, names.Distinct().Count());
    }

    [Fact]
    public async Task ExplainSetting_DescribesTheSetting_AndLinksToItOnTheSettingsScreen()
    {
        // Act
        var result = await CallAsync("explain_setting", new { name = "stores.taxcalculationenabled" });

        // Assert
        result.EnsureSucceeded();
        var json = result.Json;
        Assert.Equal(_taxCalculation.Name, json.GetProperty("name").GetString());
        Assert.Equal("Calculate sales tax", json.GetProperty("title").GetString());
        Assert.Equal("Adds tax to the orders of a store.", json.GetProperty("description").GetString());
        Assert.Equal("VirtoCommerce.Store", json.GetProperty("module_id").GetString());
        Assert.Equal("Stores > General", json.GetProperty("group").GetString());
        Assert.Equal("Boolean", json.GetProperty("value_type").GetString());
        Assert.True(json.GetProperty("value").GetBoolean());
        Assert.True(json.GetProperty("default_value").GetBoolean());
        Assert.False(json.GetProperty("is_changed").GetBoolean());
        Assert.False(json.GetProperty("is_secret").GetBoolean());
        Assert.Equal(["Store"], json.GetProperty("own_values_in").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("Each Store keeps its own value of this setting; this is the platform-wide value.", GetNotes(result));
        Assert.Equal($"Settings > Stores > General ({Origin}/#!/workspace/settings?setting=Stores.TaxCalculationEnabled).",
            json.GetProperty("back_office").GetString());
    }

    [Theory]
    [InlineData("Payment.Stripe.ApiKey", "sk_live_4242")]
    [InlineData("Notifications.Smtp.Signature", "smtp-signature-0042")]
    [InlineData("Erp.SigningCode", "erp-signing-0042")]
    public async Task ExplainSetting_ASecret_IsExplainedWithoutItsValues(string name, string savedValue)
    {
        // Act
        var result = await CallAsync("explain_setting", new { name });

        // Assert
        result.EnsureSucceeded();
        Assert.True(result.Json.GetProperty("is_secret").GetBoolean());
        Assert.False(result.Json.TryGetProperty("value", out _));
        Assert.False(result.Json.TryGetProperty("default_value", out _));
        Assert.DoesNotContain(savedValue, result.Text);
        Assert.DoesNotContain("sk_test_0000", result.Text);
        Assert.Contains("A person enters it in the back office", Assert.Single(GetNotes(result)));
        Assert.EndsWith($"/#!/workspace/settings?setting={name}).", result.Json.GetProperty("back_office").GetString());
    }

    [Fact]
    public async Task ExplainSetting_SaysWhenTheConfigurationSetsTheValue_AndWhenAChangeNeedsARestart()
    {
        // Act
        var senderEmail = await CallAsync("explain_setting", new { name = _senderEmail.Name });
        var cacheExpiration = await CallAsync("explain_setting", new { name = _cacheExpiration.Name });

        // Assert
        Assert.True(senderEmail.Json.GetProperty("is_set_by_configuration").GetBoolean());
        Assert.Contains(GetNotes(senderEmail), x => x.Contains("the back office shows it but cannot change it"));
        Assert.True(cacheExpiration.Json.GetProperty("restart_required").GetBoolean());
        Assert.Equal(["A change takes effect after the platform restarts."], GetNotes(cacheExpiration));
    }

    [Fact]
    public async Task ExplainSetting_ListsTheAllowedValues_OrTheItemsOfADictionary()
    {
        // Act
        var orderStatus = await CallAsync("explain_setting", new { name = _orderStatus.Name });
        var imageCategories = await CallAsync("explain_setting", new { name = _imageCategories.Name });

        // Assert
        Assert.Equal(["New", "Processing"], orderStatus.Json.GetProperty("allowed_values").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Processing", orderStatus.Json.GetProperty("value").GetString());

        Assert.Equal(["Images", "Thumbnails", "Banners"], imageCategories.Json.GetProperty("value").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(["Images", "Thumbnails"], imageCategories.Json.GetProperty("default_value").EnumerateArray().Select(x => x.GetString()));
        Assert.True(imageCategories.Json.GetProperty("is_changed").GetBoolean());
        Assert.Contains("The value is a list of items, edited on the Settings screen.", GetNotes(imageCategories));
    }

    // Whoever calls the service gets a secret without its values: they are not even read.
    [Fact]
    public async Task GetSettingAsync_ASecret_HasNoValues_WhichAreNotRead()
    {
        // Arrange
        var secretOptions = new McpSecretSettingsOptions();
        var service = new McpSettingsService(_settingsPropertyService, _translationService.Object, Options.Create(secretOptions));

        // Act
        var setting = await service.GetSettingAsync(_stripeKey.Name);

        // Assert
        Assert.True(setting.IsSecret);
        Assert.Equal("Stripe API key", setting.Title);
        Assert.Null(setting.Value);
        Assert.Null(setting.DefaultValue);
        Assert.False(setting.IsChanged);
        _settingsManager.Verify(x => x.GetObjectSettingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("Catalog.NoSuchSetting")]
    [InlineData("Platform.Internal.Marker")]
    public async Task ExplainSetting_ANameTheBackOfficeDoesNotShow_IsNotFound(string name)
    {
        var result = await CallAsync("explain_setting", new { name });

        Assert.Equal(McpToolCallOutcome.Failed, result.Outcome);
        Assert.Equal($"Setting '{name}' was not found. search_settings finds settings by words in their name, title or description.", result.Error);
    }

    private Task<McpToolCallResult> CallAsync(string toolName, object arguments)
    {
        return _harness.CallAsync(_settingsReader, toolName, arguments, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static List<string> GetNames(McpToolCallResult result)
    {
        return result.Json.GetProperty("settings").GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
    }

    private static JsonElement GetRow(McpToolCallResult result, string name)
    {
        return result.Json.GetProperty("settings").GetProperty("items").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
    }

    private static List<string> GetNotes(McpToolCallResult result)
    {
        return result.Json.GetProperty("notes").EnumerateArray().Select(x => x.GetString()).ToList();
    }

    private static SettingDescriptor Setting(string name, string moduleId, string groupName, SettingValueType valueType, object defaultValue)
    {
        return new SettingDescriptor { Name = name, ModuleId = moduleId, GroupName = groupName, ValueType = valueType, DefaultValue = defaultValue };
    }

    private static ObjectSettingEntry GetSavedSetting(string name)
    {
        var descriptor = _descriptors.Single(x => x.Name == name);
        var setting = new ObjectSettingEntry(descriptor);

        if (_saved.TryGetValue(name, out var value))
        {
            if (descriptor.IsDictionary)
            {
                setting.AllowedValues = (object[])value;
            }
            else
            {
                setting.Value = value;
            }
        }

        return setting;
    }

    // The configuration sets the values of the named settings.
    private sealed class FixedOverrides(params string[] settingNames) : ISettingsOverrideProvider
    {
        public bool TryGetCurrentValue(SettingDescriptor descriptor, string objectType, string objectId, out object value)
        {
            value = null;
            return settingNames.Contains(descriptor.Name);
        }

        public bool TryGetDefaultValue(SettingDescriptor descriptor, string objectType, string objectId, out object value)
        {
            value = null;
            return false;
        }
    }
}
