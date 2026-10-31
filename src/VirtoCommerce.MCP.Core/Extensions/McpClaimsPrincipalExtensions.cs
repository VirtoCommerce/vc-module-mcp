using System;
using System.Linq;
using System.Security.Claims;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core;

namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpClaimsPrincipalExtensions
{
    private const char ScopeSeparator = '|';

    // How the permission is granted to the user, or null when it is not granted. A permission claim is either the bare
    // permission name (global) or the name followed by '|' and the JSON array of its scopes. The scopes are read as
    // type and value only, so a tool does not depend on the scope classes of the module that defines them.
    public static McpPermissionGrant GetMcpPermissionGrant(this ClaimsPrincipal user, string permission)
    {
        if (user == null || string.IsNullOrEmpty(permission))
        {
            return null;
        }

        if (user.IsInRole(PlatformConstants.Security.SystemRoles.Administrator))
        {
            return new McpPermissionGrant { Permission = permission, IsGlobal = true };
        }

        McpPermissionGrant grant = null;

        foreach (var value in user.FindAll(PlatformConstants.Security.Claims.PermissionClaimType).Select(x => x.Value))
        {
            var separatorIndex = value.IndexOf(ScopeSeparator);
            var name = separatorIndex < 0 ? value : value[..separatorIndex];
            if (!string.Equals(name, permission, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            grant ??= new McpPermissionGrant { Permission = permission };

            if (separatorIndex < 0)
            {
                grant.IsGlobal = true;
                continue;
            }

            foreach (var scope in ParseScopes(value[(separatorIndex + 1)..]))
            {
                grant.Scopes.Add(scope);
            }
        }

        // A scoped claim that cannot be read grants no scope at all, never the global permission.
        return grant;
    }

    private static McpPermissionScope[] ParseScopes(string json)
    {
        try
        {
            return JArray.Parse(json)
                .OfType<JObject>()
                .Select(x => new McpPermissionScope
                {
                    Type = (string)(x["Type"] ?? x["type"]),
                    Scope = (string)(x["Scope"] ?? x["scope"]),
                })
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
