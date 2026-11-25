using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

// A setting described for a person: what it means, its type, its default and its value. A secret is described without its
// values. A toolset derives it to add what it knows about the setting, as McpSetupSetting does for a setup.
public class McpSetting
{
    public string Name { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string ModuleId { get; set; }

    // The name of the SettingValueType: Boolean, ShortText, Integer and so on.
    public string ValueType { get; set; }

    public object DefaultValue { get; set; }

    public object Value { get; set; }

    // The values a person chooses from, when the setting limits them.
    public IList<object> AllowedValues { get; set; }

    public bool IsRequired { get; set; }

    // Whether the value differs from the default.
    public bool IsChanged { get; set; }
}
