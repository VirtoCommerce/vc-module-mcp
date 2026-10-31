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

    // Switches a tool, or with * a whole scope, on or off in one scope, keeping the other entries of the setting.
    public virtual async Task SetEnabledAsync(string scopeName, string toolName, bool enabled, IEnumerable<string> allScopeNames)
    {
        var setting = await settingsManager.GetObjectSettingAsync(ModuleConstants.Settings.Tools.Disabled.Name);
        setting.Value = UpdateSetting(setting.Value as string, scopeName, toolName, enabled, allScopeNames);
        await settingsManager.SaveObjectSettingsAsync([setting]);
    }

    // Entries that switch the tool off in every scope (tool_name, or * for every tool) are split into one entry per other
    // scope when the tool is switched back on in one scope, so the other scopes keep it off.
    public static string UpdateSetting(string setting, string scopeName, string toolName, bool enabled, IEnumerable<string> allScopeNames)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopeName);
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        var entries = (setting ?? string.Empty)
            .Split(_entrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var scopeEntry = $"{scopeName}/{toolName}";

        if (!enabled)
        {
            if (!IsDisabled(ParseDisabledToolNames(setting, scopeName), toolName))
            {
                entries.Add(scopeEntry);
            }

            return string.Join('\n', entries);
        }

        entries.RemoveAll(x => string.Equals(x, scopeEntry, StringComparison.OrdinalIgnoreCase));

        if (entries.RemoveAll(x => string.Equals(x, toolName, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            entries.AddRange((allScopeNames ?? [])
                .Where(x => !string.Equals(x, scopeName, StringComparison.OrdinalIgnoreCase))
                .Select(x => $"{x}/{toolName}")
                .Where(x => !entries.Contains(x, StringComparer.OrdinalIgnoreCase)));
        }

        return string.Join('\n', entries);
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
