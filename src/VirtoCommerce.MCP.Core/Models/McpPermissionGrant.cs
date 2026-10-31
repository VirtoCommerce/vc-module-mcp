using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.Core.Models;

// How a permission is granted to the user: globally, or limited by permission scopes.
public class McpPermissionGrant
{
    public string Permission { get; set; }

    // The user has the permission without limits: through a role permission without scopes, or as an administrator.
    public bool IsGlobal { get; set; }

    public IList<McpPermissionScope> Scopes { get; set; } = [];

    // Values of the scopes of one type, for example the store ids of MarketingStoreSelectedScope. Check IsGlobal
    // first: a global permission has no scopes and allows every value, so this list is empty for it.
    public IList<string> GetScopeValues(string scopeType)
    {
        return Scopes
            .Where(x => string.Equals(x.Type, scopeType, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Scope))
            .Select(x => x.Scope)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Whether a value of a scope type is allowed: always for a global permission.
    public bool Allows(string scopeType, string value)
    {
        return IsGlobal || GetScopeValues(scopeType).Contains(value, StringComparer.OrdinalIgnoreCase);
    }
}

public class McpPermissionScope
{
    public string Type { get; set; }

    public string Scope { get; set; }
}
