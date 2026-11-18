using System;
using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

// Settings that are secrets whatever their name and type. The tools that describe settings never list them and never
// return their values. Filled with AddMcpSecretSettings.
public class McpSecretSettingsOptions
{
    public ISet<string> SettingNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
