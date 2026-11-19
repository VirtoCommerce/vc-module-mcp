using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Settings;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Localizations;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Web.Services;

// The platform's settings as the settings tools describe them: the settings the back office shows, with their
// platform-wide values, from the platform's Settings V2 service; titles and descriptions from the modules' English
// localization files (McpSettingTexts). A secret (McpSecretSettings) is never listed and is described without its values.
public class McpSettingsService(
    ISettingsPropertyService settingsPropertyService,
    ITranslationService translationService,
    IOptions<McpSecretSettingsOptions> secretOptions)
{
    // The settings the back office shows, without secrets, with their platform-wide values, in the order of the Settings
    // screen: by group, then by name.
    public virtual async Task<IList<McpPlatformSetting>> GetSettingsAsync()
    {
        var schemas = await settingsPropertyService.GetSchemaAsync(AbstractTypeFactory<SettingsPropertySearchCriteria>.TryCreateInstance());
        var changedValues = await settingsPropertyService.GetValuesAsync(modifiedOnly: true);
        var translation = translationService.GetTranslationDataForLanguage(McpSettingTexts.Language);

        return schemas
            .Where(x => !IsSecret(x))
            .Select(x => ToSetting(x, changedValues, translation))
            .ToList();
    }

    // One setting the back office shows, a secret too, without its values; null for a name the back office does not show.
    public virtual async Task<McpPlatformSetting> GetSettingAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var settingName = name.Trim();

        // The platform builds the schemas of the settings whose name, display name or group holds the name only: the
        // setting itself and a few others.
        var criteria = AbstractTypeFactory<SettingsPropertySearchCriteria>.TryCreateInstance();
        criteria.Keyword = settingName;

        var schemas = await settingsPropertyService.GetSchemaAsync(criteria);
        var schema = schemas.FirstOrDefault(x => x.Name.EqualsIgnoreCase(settingName));

        if (schema == null)
        {
            return null;
        }

        // A secret's values are not even read.
        var changedValues = IsSecret(schema)
            ? new Dictionary<string, object>()
            : await settingsPropertyService.GetValuesAsync(modifiedOnly: true, moduleId: schema.ModuleId);

        return ToSetting(schema, changedValues, translationService.GetTranslationDataForLanguage(McpSettingTexts.Language));
    }

    public virtual bool IsSecret(SettingPropertySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return McpSecretSettings.IsSecret(schema.Name, schema.ValueType, secretOptions.Value);
    }

    // changedValues holds the values that differ from the defaults, as the Settings V2 service compares them; every other
    // setting has its default value.
    protected virtual McpPlatformSetting ToSetting(SettingPropertySchema schema, IDictionary<string, object> changedValues, JObject translation)
    {
        var (title, description) = McpSettingTexts.Find(translation, schema.Name, schema.DisplayName);

        var setting = AbstractTypeFactory<McpPlatformSetting>.TryCreateInstance();
        setting.Name = schema.Name;
        setting.Title = title;
        setting.Description = description;
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
}
