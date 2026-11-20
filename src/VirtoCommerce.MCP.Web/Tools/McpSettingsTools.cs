using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Web.Tools;

// Read-only tools over the platform's settings, for every role: what a setting means, its platform-wide value, and the
// back-office screen that changes it. No tool changes a setting. Secrets are never listed and their values never returned.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
[Authorize(PlatformConstants.Security.Permissions.SettingQuery)]
public class McpSettingsTools(
    McpSettingsService settingsService,
    IMcpPageSizeResolver pageSizeResolver,
    IMcpLinkResolver linkResolver)
{
    // The back office's Settings screen; ?setting= scrolls to one setting and highlights it.
    public const string SettingsRoute = "/#!/workspace/settings";

    [McpServerTool(Name = ModuleConstants.ToolNames.SearchSettings, ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Find platform-wide settings of the installed modules by words in their name, title, description or group. " +
        "Each row has the title, description, module, group, type, current platform-wide value and default, and whether the " +
        "value was changed. Filter by module, or to the settings changed from their defaults. Secrets are not listed: a person " +
        "enters them in the back office. Use explain_setting for one setting's details and the screen that changes it. " +
        "Returns one page: when has_more is true, call again with next_skip.")]
    public async Task<object> SearchSettings(
        [Description("Words to find in the name, title, description or group, such as \"tax calculation\". Every word must be found. Omit it to list every setting.")] string keyword = null,
        [Description("Module id, such as VirtoCommerce.Catalog, to search that module's settings only.")] string module_id = null,
        [Description("true to list only the settings whose values differ from their defaults.")] bool changed_only = false,
        [Description("Number of settings to skip. Use next_skip of the previous page.")] int? skip = null,
        [Description("Number of settings to return. Omit it for the default page size; larger values are capped.")] int? take = null)
    {
        var paging = await pageSizeResolver.GetPagingAsync(skip, take);
        var settings = await settingsService.GetSettingsAsync();

        if (!string.IsNullOrWhiteSpace(module_id))
        {
            settings = settings.Where(x => x.ModuleId.EqualsIgnoreCase(module_id.Trim())).ToList();

            // A wrong module id would otherwise read as a module without settings. A module whose settings are all secrets
            // gets an empty page, with the note on secrets.
            if (settings.Count == 0 && !await settingsService.HasSettingsAsync(module_id))
            {
                throw new McpException($"Module '{module_id}' has no settings here. Module ids look like VirtoCommerce.Catalog; " +
                    "search_settings without module_id finds the settings of every module.");
            }
        }

        var words = (keyword ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = settings
            .Where(x => words.All(word => HasWord(x, word)))
            .Where(x => !changed_only || x.IsChanged);

        return new
        {
            settings = found.ToMcpPage(paging, ToRow),
            secrets = AddLink("Secrets are not listed. A person enters them in the back office: Settings", await GetSettingsLinkAsync()),
            next_step = "explain_setting tells what one setting does, the values it allows, and where to change it.",
        };
    }

    [McpServerTool(Name = ModuleConstants.ToolNames.ExplainSetting, ReadOnly = true, Destructive = false, Idempotent = true)]
    [Description("Explain one platform setting: what it does, its type, its platform-wide value and default, the values it allows, " +
        "whether the platform's configuration sets it, whether a change needs a restart, whether objects such as stores keep " +
        "their own values, and the back-office screen that changes it, with a link. A secret is explained without its value. " +
        "Find the name with search_settings.")]
    public async Task<object> ExplainSetting(
        [Description("The setting's name, as search_settings returns it, such as Catalog.ImageCategories.")] string name)
    {
        var setting = await settingsService.GetSettingAsync(name)
            ?? throw new McpException($"Setting '{name}' was not found. search_settings finds settings by words in their name, title, description or group.");

        var backOffice = AddLink(GetScreen(setting), await GetSettingsLinkAsync(setting.Name));

        if (setting.IsSecret)
        {
            return new
            {
                name = setting.Name,
                title = setting.Title,
                description = setting.Description,
                module_id = setting.ModuleId,
                group = GetGroupPath(setting.GroupName),
                value_type = setting.ValueType,
                is_secret = true,
                notes = new[] { "A secret: its value is never shown here and never accepted through an agent. A person enters it in the back office." },
                back_office = backOffice,
            };
        }

        return new
        {
            name = setting.Name,
            title = setting.Title,
            description = setting.Description,
            module_id = setting.ModuleId,
            group = GetGroupPath(setting.GroupName),
            value_type = setting.ValueType,
            value = setting.Value,
            default_value = setting.DefaultValue,
            is_changed = setting.IsChanged,
            allowed_values = setting.AllowedValues,
            is_required = setting.IsRequired,
            is_secret = false,
            is_set_by_configuration = setting.IsSetByConfiguration,
            restart_required = setting.RestartRequired,
            own_values_in = setting.TenantTypes,
            notes = GetNotes(setting),
            back_office = backOffice,
        };
    }

    // A link to the Settings screen, opened at one setting when a name is given; null outside an MCP request or when the
    // scope has no public origin.
    protected virtual Task<string> GetSettingsLinkAsync(string settingName = null)
    {
        var route = string.IsNullOrEmpty(settingName) ? SettingsRoute : $"{SettingsRoute}?setting={Uri.EscapeDataString(settingName)}";

        return linkResolver.GetLinkAsync(route);
    }

    protected virtual IList<string> GetNotes(McpPlatformSetting setting)
    {
        var notes = new List<string>();

        if (setting.IsSetByConfiguration)
        {
            notes.Add("The platform's configuration (appsettings or environment variables) sets this value: the back office shows it but cannot change it.");
        }

        if (setting.RestartRequired)
        {
            notes.Add("A change takes effect after the platform restarts.");
        }

        notes.AddRange(setting.TenantTypes.Select(type => $"Each {type} keeps its own value of this setting; this is the platform-wide value."));

        if (setting.IsDictionary)
        {
            notes.Add(setting.IsLocalizable
                ? "The value is a list of items, each with a name per language, edited on the Settings screen."
                : "The value is a list of items, edited on the Settings screen.");
        }

        return notes;
    }

    protected virtual object ToRow(McpPlatformSetting setting)
    {
        return new
        {
            name = setting.Name,
            title = setting.Title,
            description = setting.Description,
            module_id = setting.ModuleId,
            group = GetGroupPath(setting.GroupName),
            value_type = setting.ValueType,
            value = setting.Value,
            default_value = setting.DefaultValue,
            is_changed = setting.IsChanged,
        };
    }

    // A word of a search: in the name, the title, the description, or the group as the answers show it ("Catalog > General").
    protected virtual bool HasWord(McpPlatformSetting setting, string word)
    {
        return Contains(setting.Name, word) || Contains(setting.Title, word) || Contains(setting.Description, word) || Contains(GetGroupPath(setting.GroupName), word);
    }

    private static bool Contains(string text, string word)
    {
        return text?.ContainsIgnoreCase(word) == true;
    }

    // "Catalog|General" reads "Catalog > General", as the Settings screen nests it.
    protected virtual string GetGroupPath(string groupName)
    {
        return string.IsNullOrEmpty(groupName) ? null : string.Join(" > ", groupName.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    protected virtual string GetScreen(McpPlatformSetting setting)
    {
        var group = GetGroupPath(setting.GroupName);

        return string.IsNullOrEmpty(group) ? "Settings" : $"Settings > {group}";
    }

    private static string AddLink(string text, string link)
    {
        return $"{text.WithLink(link)}.";
    }
}
