using System.Collections.Generic;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Models;

// A platform setting as the settings tools describe it: its platform-wide value, where the Settings screen shows it and
// how it is set. A secret has no values here.
public class McpPlatformSetting : McpSetting
{
    // The group of the Settings screen, as the module registered it: "Catalog|General".
    public string GroupName { get; set; }

    public bool IsSecret { get; set; }

    // A dictionary setting: its value is the list of items people edit, its default the list the module registered, and
    // it has no allowed values.
    public bool IsDictionary { get; set; }

    public bool IsLocalizable { get; set; }

    // The platform's configuration (appsettings, environment variables) sets the value, so the back office cannot change it
    // and the settings tools do not show it.
    public bool IsSetByConfiguration { get; set; }

    public bool RestartRequired { get; set; }

    // The types whose objects keep their own value of the setting, such as Store.
    public IList<string> TenantTypes { get; set; } = [];
}
