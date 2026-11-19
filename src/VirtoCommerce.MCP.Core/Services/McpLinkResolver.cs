using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Extensions;

namespace VirtoCommerce.MCP.Core.Services;

public class McpLinkResolver(IHttpContextAccessor httpContextAccessor) : IMcpLinkResolver
{
    public virtual async Task<string> GetLinkAsync(string route)
    {
        var context = httpContextAccessor.HttpContext;
        var scope = context.GetMcpScope();

        if (scope == null)
        {
            return null;
        }

        var origin = await context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope).GetOriginAsync(context, scope);

        return string.IsNullOrEmpty(origin) ? null : $"{origin.TrimEnd('/')}{route}";
    }
}
