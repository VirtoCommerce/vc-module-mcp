using System.Collections.Generic;

namespace VirtoCommerce.MCP.Web.Models;

// A platform setting described for a person: what it means, its platform-wide value and how it is set. A secret has no
// values here.
public class McpSetting
{
    public string Name { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string ModuleId { get; set; }

    // The group of the Settings screen, as the module registered it: "Catalog|General".
    public string GroupName { get; set; }

    // The name of the SettingValueType: Boolean, ShortText, Integer and so on.
    public string ValueType { get; set; }

    // For a dictionary setting, its items: the value is the list people edit, the default the list the module registered.
    public object DefaultValue { get; set; }

    public object Value { get; set; }

    // The values a person chooses from, for a setting that is not a dictionary.
    public IList<object> AllowedValues { get; set; }

    // Whether the value differs from the default.
    public bool IsChanged { get; set; }

    public bool IsSecret { get; set; }

    public bool IsRequired { get; set; }

    public bool IsDictionary { get; set; }

    public bool IsLocalizable { get; set; }

    // The platform's configuration (appsettings, environment variables) sets the value, so the back office cannot change it.
    public bool IsSetByConfiguration { get; set; }

    public bool RestartRequired { get; set; }

    // The types whose objects keep their own value of the setting, such as Store.
    public IList<string> TenantTypes { get; set; } = [];
}
