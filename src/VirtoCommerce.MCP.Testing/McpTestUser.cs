using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using VirtoCommerce.Platform.Core;

namespace VirtoCommerce.MCP.Testing;

// A signed-in user for tool tests, with the claims the platform issues: permission claims, global or limited by
// permission scopes, and the administrator role. Build it fluently and pass it where a ClaimsPrincipal is expected:
//   McpTestUser.Create("marketer").WithPermissions("marketing:read").WithScopedPermission("marketing:update", "MarketingStoreSelectedScope", "fashion")
public sealed class McpTestUser
{
    private readonly List<Claim> _claims = [];

    private McpTestUser(string userName, string userId)
    {
        UserName = userName;
        UserId = userId ?? userName;
        _claims.Add(new Claim(ClaimTypes.NameIdentifier, UserId));
        _claims.Add(new Claim("sub", UserId));
        _claims.Add(new Claim(ClaimTypes.Name, userName));
    }

    public string UserName { get; }

    public string UserId { get; }

    public static McpTestUser Create(string userName = "test-user", string userId = null)
    {
        return new McpTestUser(userName, userId);
    }

    // A user with the permissions granted globally.
    public static ClaimsPrincipal WithPermissions(string userName, params string[] permissions)
    {
        return Create(userName).WithPermissions(permissions);
    }

    public static ClaimsPrincipal Administrator(string userName = "admin")
    {
        return Create(userName).AsAdministrator();
    }

    public static ClaimsPrincipal Anonymous()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    public McpTestUser WithPermissions(params string[] permissions)
    {
        _claims.AddRange((permissions ?? []).Select(x => new Claim(PlatformConstants.Security.Claims.PermissionClaimType, x)));
        return this;
    }

    // A permission limited by a permission scope, such as the stores of MarketingStoreSelectedScope: the claim the
    // platform issues for a role permission with scopes, "permission|[{"Type":"...","Scope":"..."}]".
    public McpTestUser WithScopedPermission(string permission, string scopeType, params string[] scopeValues)
    {
        var scopes = (scopeValues ?? []).Select(x => new Dictionary<string, string> { ["Type"] = scopeType, ["Scope"] = x });
        _claims.Add(new Claim(PlatformConstants.Security.Claims.PermissionClaimType, $"{permission}|{JsonSerializer.Serialize(scopes)}"));
        return this;
    }

    public McpTestUser AsAdministrator()
    {
        _claims.Add(new Claim(ClaimTypes.Role, PlatformConstants.Security.SystemRoles.Administrator));
        return this;
    }

    public McpTestUser WithClaim(string type, string value)
    {
        _claims.Add(new Claim(type, value));
        return this;
    }

    public ClaimsPrincipal ToPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(_claims, "mcp-test", ClaimTypes.Name, ClaimTypes.Role));
    }

    public static implicit operator ClaimsPrincipal(McpTestUser user)
    {
        return user?.ToPrincipal();
    }
}
