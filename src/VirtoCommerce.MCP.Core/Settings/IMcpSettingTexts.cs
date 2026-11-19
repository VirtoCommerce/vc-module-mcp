using Newtonsoft.Json.Linq;

namespace VirtoCommerce.MCP.Core.Settings;

// Where the titles and descriptions of settings come from, for every tool that describes settings. A solution changes the
// language or the source for all of them by registering its own.
public interface IMcpSettingTexts
{
    // The texts of every setting: read once for an answer, then passed to Find for each setting.
    JObject GetTranslation();

    // The title and the description of a setting in the texts from GetTranslation. A title the texts do not have is the
    // display name, or else the name; such a description is null.
    (string Title, string Description) Find(JObject translation, string settingName, string displayName = null);
}
