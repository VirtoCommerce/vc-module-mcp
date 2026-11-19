using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Core.Settings;

// The rule that keeps secrets out of the answers of every tool that describes settings.
public static partial class McpSecretSettings
{
    // Compared with the whole words of a setting name: "ApiKey" has the word Key, "Keyword" does not.
    private static readonly HashSet<string> _secretWords = new(StringComparer.OrdinalIgnoreCase) { "Key", "Secret", "Password", "Token", "Credential", "Credentials" };

    // A setting is a secret when it is typed SecureString, when a module names it (AddMcpSecretSettings), or when its name
    // has the word Key, Secret, Password, Token or Credential(s). The rule errs on the safe side.
    public static bool IsSecret(string name, SettingValueType valueType, McpSecretSettingsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return valueType == SettingValueType.SecureString
            || options.SettingNames.Contains(name)
            || GetNameWords(name).Any(_secretWords.Contains);
    }

    // The words of a name, split at dots and case changes: "Stripe.APIKey" has Stripe, API and Key.
    private static IEnumerable<string> GetNameWords(string name)
    {
        return string.IsNullOrEmpty(name) ? [] : NameWordRegex().Matches(name).Select(x => x.Value);
    }

    [GeneratedRegex("[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z0-9]+|[A-Z]+")]
    private static partial Regex NameWordRegex();
}
