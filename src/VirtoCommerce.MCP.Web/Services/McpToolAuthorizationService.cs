using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace VirtoCommerce.MCP.Web.Services;

public class McpToolAuthorizationService(
    IAuthorizationPolicyProvider policyProvider,
    IAuthorizationService authorizationService)
{
    public virtual async Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, IMcpServerPrimitive primitive)
    {
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
}
