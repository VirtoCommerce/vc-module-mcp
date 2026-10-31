using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Services;

public class McpToolAuthorizationService(
    IAuthorizationPolicyProvider policyProvider,
    IAuthorizationService authorizationService)
{
    public virtual async Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, IMcpServerPrimitive primitive)
    {
        if (!HasMcpPermissions(user, primitive))
        {
            return false;
        }

        if (primitive.Metadata.OfType<IAllowAnonymous>().Any())
        {
            return true;
        }

        var authorizeData = primitive.Metadata.OfType<IAuthorizeData>().ToList();
        if (authorizeData.Count == 0)
        {
            return true;
        }

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);
        if (policy == null)
        {
            return true;
        }

        var result = await authorizationService.AuthorizeAsync(user ?? new ClaimsPrincipal(new ClaimsIdentity()), resource: null, policy);

        return result.Succeeded;
    }

    // An approver of the scope: a user with its ApprovalPermission.
    public virtual async Task<bool> IsApproverAsync(ClaimsPrincipal user, McpScope scope)
    {
        if (scope?.SupportsApproval != true || user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var policy = await policyProvider.GetPolicyAsync(scope.ApprovalPermission);
        return policy != null && (await authorizationService.AuthorizeAsync(user, resource: null, policy)).Succeeded;
    }

    // [McpPermission]: every permission must be granted, globally or with scopes.
    public virtual bool HasMcpPermissions(ClaimsPrincipal user, IMcpServerPrimitive primitive)
    {
        return primitive.Metadata
            .OfType<McpPermissionAttribute>()
            .SelectMany(x => x.Permissions)
            .All(permission => user?.Identity?.IsAuthenticated == true && user.GetMcpPermissionGrant(permission) != null);
    }
}
