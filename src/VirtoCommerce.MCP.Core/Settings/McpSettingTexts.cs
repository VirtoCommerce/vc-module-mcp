using System;
using Newtonsoft.Json.Linq;

namespace VirtoCommerce.MCP.Core.Settings;

// A setting's title and description from the modules' localization files, as the back office shows them.
public static class McpSettingTexts
{
    // Titles and descriptions come from the English localization files of the modules, as in the back office.
    public const string Language = "en";

    // The title and the description of a setting in a translation (ITranslationService.GetTranslationDataForLanguage with
    // Language). A title the files do not have is the display name, or else the name; such a description is null.
    public static (string Title, string Description) Find(JObject translation, string settingName, string displayName = null)
    {
        ArgumentNullException.ThrowIfNull(settingName);

        var texts = FindNode(translation?["settings"], settingName.Split('.'), 0);

        return (GetText(texts, "title") ?? displayName ?? settingName, GetText(texts, "description"));
    }

    private static string GetText(JToken texts, string key)
    {
        var text = texts?[key]?.ToString();

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
}
