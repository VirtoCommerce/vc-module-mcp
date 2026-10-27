using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Web.Services;

public class McpToolAvailabilityService(
    ISettingsManager settingsManager)
{
    public const string AllTools = "*";

    private static readonly char[] _entrySeparators = ['\r', '\n', ',', ';'];

    public virtual async Task<IReadOnlySet<string>> GetDisabledToolNamesAsync(string scopeName)
    {
        var setting = await settingsManager.GetValueAsync<string>(ModuleConstants.Settings.Tools.Disabled);

        return ParseDisabledToolNames(setting, scopeName);
    }

    public virtual async Task<bool> IsDisabledAsync(string scopeName, string toolName)
    {
        return IsDisabled(await GetDisabledToolNamesAsync(scopeName), toolName);
    }

    public static bool IsDisabled(IReadOnlySet<string> disabledToolNames, string toolName)
    {
        return disabledToolNames.Contains(AllTools) || (toolName != null && disabledToolNames.Contains(toolName));
    }

    public static IReadOnlySet<string> ParseDisabledToolNames(string setting, string scopeName)
    {
        return (setting ?? string.Empty)
            .Split(_entrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => GetToolNameForScope(entry, scopeName))
            .Where(toolName => !string.IsNullOrEmpty(toolName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetToolNameForScope(string entry, string scopeName)
    {
        var separator = entry.IndexOf('/');
        if (separator < 0)
        {
            return entry;
        }

        var entryScope = entry[..separator].Trim();
        return string.Equals(entryScope, scopeName, StringComparison.OrdinalIgnoreCase)
            ? entry[(separator + 1)..].Trim()
            : null;
    }
}
