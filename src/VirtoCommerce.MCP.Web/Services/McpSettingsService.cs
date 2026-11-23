using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using VirtoCommerce.MCP.Core.Settings;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Web.Services;

// The platform's settings as the settings tools describe them: the settings the back office shows, with their
// platform-wide values, from the platform's Settings V2 service; titles and descriptions from IMcpSettingTexts. A secret
// (IMcpSecretSettings) is never listed and is described without its values. A setting the platform's configuration sets
// is never listed either, and a default the configuration sets is the setting's default.
public class McpSettingsService(
    ISettingsPropertyService settingsPropertyService,
    IMcpSecretSettings secretSettings,
    IMcpSettingTexts settingTexts,
    ISettingsOverrideProvider settingsOverrideProvider,
    IOptions<FixedSettings> fixedSettings)
{
    // The settings the back office shows, without secrets and the settings the configuration sets, with their
    // platform-wide values, in the order of the Settings screen: by group, then by name.
    public virtual async Task<IList<McpPlatformSetting>> GetSettingsAsync()
    {
        var schemas = await settingsPropertyService.GetSchemaAsync(AbstractTypeFactory<SettingsPropertySearchCriteria>.TryCreateInstance());
        // One read for the changed values of every setting, secrets' too: the secrets and the settings the configuration
        // sets are dropped before any is described.
        var changedValues = await settingsPropertyService.GetValuesAsync(modifiedOnly: true);
        var translation = settingTexts.GetTranslation();

        return schemas
            .Where(x => !IsSecret(x) && !IsSetByConfiguration(x))
            .Select(x => ToSetting(x, changedValues, translation))
            .ToList();
    }

    // Whether the back office shows settings of the module, secrets and the settings the configuration sets included: a
    // module whose settings are all left out is a module, not a wrong id.
    public virtual async Task<bool> HasSettingsAsync(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return false;
        }

        var schemas = await settingsPropertyService.GetSchemaAsync(AbstractTypeFactory<SettingsPropertySearchCriteria>.TryCreateInstance());

        return schemas.Any(x => x.ModuleId.EqualsIgnoreCase(moduleId.Trim()));
    }

    // One setting the back office shows, a secret too, without its values, and a setting the configuration sets, marked
    // IsSetByConfiguration; null for a name the back office does not show.
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

        return ToSetting(schema, changedValues, settingTexts.GetTranslation());
    }

    public virtual bool IsSecret(SettingPropertySchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return secretSettings.IsSecret(schema.Name, schema.ValueType);
    }

    // The configuration forces the setting's value: by VirtoCommerce:Settings:Override:CurrentValue, which the schema
    // marks read-only, or as a fixed setting of the PlatformSettings section, which the schema does not mark but the
    // settings manager serves read-only and never saves.
    protected virtual bool IsSetByConfiguration(SettingPropertySchema schema)
    {
        return schema.IsReadOnly || fixedSettings.Value.Settings?.Any(x => x.Name.EqualsIgnoreCase(schema.Name)) == true;
    }

    // changedValues holds the values that differ from the defaults, as the Settings V2 service compares them; every other
    // setting has its default value.
    protected virtual McpPlatformSetting ToSetting(SettingPropertySchema schema, IDictionary<string, object> changedValues, JObject translation)
    {
        var (title, description) = settingTexts.Find(translation, schema.Name, schema.DisplayName);

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
        setting.IsSetByConfiguration = IsSetByConfiguration(schema);
        setting.RestartRequired = schema.RestartRequired;
        setting.TenantTypes = schema.AssignedToTenants?.ToList() ?? [];

        // A secret is described without its values, whoever asks.
        if (!setting.IsSecret)
        {
            var defaultValue = GetDefaultValue(schema);

            setting.IsChanged = changedValues.TryGetValue(schema.Name, out var changedValue);
            setting.DefaultValue = defaultValue;
            setting.Value = setting.IsChanged ? changedValue : defaultValue;
            setting.AllowedValues = schema.IsDictionary ? null : schema.AllowedValues?.ToList();
        }

        return setting;
    }

    // The default the Settings V2 service compares the value with: the one the configuration sets
    // (VirtoCommerce:Settings:Override:DefaultValue), else the module's, the only one the schema holds. A dictionary
    // setting's default is the list of items the module registered, which the service compares the items with.
    protected virtual object GetDefaultValue(SettingPropertySchema schema)
    {
        if (schema.IsDictionary)
        {
            return schema.AllowedValues;
        }

        // The platform-wide default, for no object, as the settings manager asks for it when it reads the values the service
        // compares; the configuration finds it by the setting's name and converts it to the setting's type.
        var descriptor = new SettingDescriptor { Name = schema.Name, ValueType = schema.ValueType };

        return settingsOverrideProvider.TryGetDefaultValue(descriptor, objectType: null, objectId: null, out var configuredDefault)
            ? configuredDefault
            : schema.DefaultValue;
    }
}
