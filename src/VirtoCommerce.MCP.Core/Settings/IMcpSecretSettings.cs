using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Core.Settings;

// The rule that keeps secrets out of the answers of every tool that describes settings: the MCP module's settings tools
// and the toolsets of other modules, such as Store's setup. A solution changes it for all of them by registering its own.
public interface IMcpSecretSettings
{
    // A secret is never listed, accepted or returned, and is described without its values.
    bool IsSecret(string name, SettingValueType valueType);
}
