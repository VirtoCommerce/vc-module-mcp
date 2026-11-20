using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Core.Settings;

// The MCP module's secret rule (IMcpSecretSettings).
public partial class McpSecretSettings(IOptions<McpSecretSettingsOptions> options) : IMcpSecretSettings
{
    // Compared with the whole words of a setting name, singular and without trailing digits: "ApiKey", "ApiKeys" and
    // "ApiKey2" have the word Key, "Keyword" does not.
    private static readonly HashSet<string> _secretWords = new(StringComparer.OrdinalIgnoreCase) { "Key", "Secret", "Password", "Token", "Credential" };

    private static readonly HashSet<string> _knownSecrets = new(McpKnownSecretSettings.SettingNames, StringComparer.OrdinalIgnoreCase);

    // A setting is a secret when it is typed SecureString, when it is known to be one (McpKnownSecretSettings), when a
    // module names it (AddMcpSecretSettings), or when its name has the word Key, Secret, Password, Token or Credential,
    // also as a plural or followed by digits. The rule errs on the safe side.
    public virtual bool IsSecret(string name, SettingValueType valueType)
    {
        return valueType == SettingValueType.SecureString
            || _knownSecrets.Contains(name)
            || options.Value.SettingNames.Contains(name)
            || GetNameWords(name).Any(IsSecretWord);
    }

    // The words of a name, split at dots and case changes: "Stripe.APIKey" has Stripe, API and Key.
    protected virtual IEnumerable<string> GetNameWords(string name)
    {
        return string.IsNullOrEmpty(name) ? [] : NameWordRegex().Matches(name).Select(x => x.Value);
    }

    // A word of a name that marks a secret: Key, Secret, Password, Token or Credential, also as a plural ("Keys") or followed
    // by digits ("Key2").
    protected virtual bool IsSecretWord(string word)
    {
        var stem = word.TrimEnd("0123456789".ToCharArray());

        return _secretWords.Contains(stem) || (stem.EndsWithIgnoreCase("s") && _secretWords.Contains(stem[..^1]));
    }

    [GeneratedRegex("[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z0-9]+|[A-Z]+")]
    private static partial Regex NameWordRegex();
}
