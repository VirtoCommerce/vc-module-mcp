using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Localizations;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Web.Services;

// The platform's settings as the settings tools describe them: the settings the back office shows, with their
// platform-wide values, from the platform's Settings V2 service; titles and descriptions from the modules' English
// localization files. A secret is never listed and is described without its values. The Store module has the same title
// lookup and secret rule for its store setup tools: the two modules share no code.
public partial class McpSettingsService(
    ISettingsPropertyService settingsPropertyService,
    ITranslationService translationService,
    IOptions<McpSecretSettingsOptions> secretOptions)
{
    // Titles and descriptions come from the English localization files of the modules, as in the back office.
    private const string Language = "en";

    // Compared with the whole words of a setting name: "ApiKey" has the word Key, "Keyword" does not.
    private static readonly string[] _secretWords = ["Key", "Secret", "Password", "Token", "Credential", "Credentials"];

    // The settings the back office shows, without secrets, with their platform-wide values, in the order of the Settings
    // screen: by group, then by name.
    public virtual async Task<IList<McpSetting>> GetSettingsAsync()
    {
        var schemas = await settingsPropertyService.GetSchemaAsync(AbstractTypeFactory<SettingsPropertySearchCriteria>.TryCreateInstance());
        var changedValues = await settingsPropertyService.GetValuesAsync(modifiedOnly: true);
        var translation = translationService.GetTranslationDataForLanguage(Language);

        return schemas
            .Where(x => !IsSecret(x))
            .Select(x => ToSetting(x, changedValues, translation))
            .ToList();
    }

    // One setting the back office shows, a secret too, without its values; null for a name the back office does not show.
    public virtual async Task<McpSetting> GetSettingAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var schemas = await settingsPropertyService.GetSchemaAsync(AbstractTypeFactory<SettingsPropertySearchCriteria>.TryCreateInstance());
        var schema = schemas.FirstOrDefault(x => x.Name.EqualsIgnoreCase(name.Trim()));

        if (schema == null)
        {
            return null;
        }

        // A secret's values are not even read.
        var changedValues = IsSecret(schema)
            ? new Dictionary<string, object>()
            : await settingsPropertyService.GetValuesAsync(modifiedOnly: true, moduleId: schema.ModuleId);

        return ToSetting(schema, changedValues, translationService.GetTranslationDataForLanguage(Language));
    }

    public virtual bool IsSecret(SettingPropertySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return schema.ValueType == SettingValueType.SecureString
            || secretOptions.Value.SettingNames.Contains(schema.Name)
            || GetNameWords(schema.Name).Any(word => _secretWords.Contains(word, StringComparer.OrdinalIgnoreCase));
    }

    // changedValues holds the values that differ from the defaults, as the Settings V2 service compares them; every other
    // setting has its default value.
    protected virtual McpSetting ToSetting(SettingPropertySchema schema, IDictionary<string, object> changedValues, JObject translation)
    {
        var setting = AbstractTypeFactory<McpSetting>.TryCreateInstance();
        setting.Name = schema.Name;
        setting.Title = GetText(translation, schema.Name, "title") ?? schema.DisplayName ?? schema.Name;
        setting.Description = GetText(translation, schema.Name, "description");
        setting.ModuleId = schema.ModuleId;
        setting.GroupName = schema.GroupName;
        setting.ValueType = schema.ValueType.ToString();
        setting.IsSecret = IsSecret(schema);
        setting.IsRequired = schema.IsRequired;
        setting.IsDictionary = schema.IsDictionary;
        setting.IsLocalizable = schema.IsLocalizable;
        setting.IsSetByConfiguration = schema.IsReadOnly;
        setting.RestartRequired = schema.RestartRequired;
        setting.TenantTypes = schema.AssignedToTenants?.ToList() ?? [];

        // A secret is described without its values, whoever asks.
        if (!setting.IsSecret)
        {
            var defaultValue = schema.IsDictionary ? schema.AllowedValues : schema.DefaultValue;

            setting.IsChanged = changedValues.TryGetValue(schema.Name, out var changedValue);
            setting.DefaultValue = defaultValue;
            setting.Value = setting.IsChanged ? changedValue : defaultValue;
            setting.AllowedValues = schema.IsDictionary ? null : schema.AllowedValues?.ToList();
        }

        return setting;
    }

    private static string GetText(JObject translation, string settingName, string key)
    {
        var text = FindNode(translation?["settings"], settingName.Split('.'), 0)?[key]?.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // The localization files nest a setting name by its dots ("Stores" > "TaxCalculationEnabled"), but a key may also
    // hold several segments with their dots, so the longest key is tried first.
    private static JToken FindNode(JToken node, string[] segments, int index)
    {
        if (node is not JObject obj)
        {
            return null;
        }

        if (index == segments.Length)
        {
            return obj;
        }

        for (var end = segments.Length; end > index; end--)
        {
            if (obj.TryGetValue(string.Join('.', segments, index, end - index), out var child) && FindNode(child, segments, end) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetNameWords(string name)
    {
        return string.IsNullOrEmpty(name) ? [] : NameWordRegex().Matches(name).Select(x => x.Value);
    }

    [GeneratedRegex("[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z0-9]+|[A-Z]+")]
    private static partial Regex NameWordRegex();
}
