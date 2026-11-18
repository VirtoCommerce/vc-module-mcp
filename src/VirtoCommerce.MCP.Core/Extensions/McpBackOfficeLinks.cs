using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Core.Extensions;

// Links into the back office, for what the tools do not cover: the public origin of the request's scope, as the scope's
// IMcpPublicOriginResolver gives it, followed by the route of a screen.
public static class McpBackOfficeLinks
{
    // The link to a screen, such as "/#!/workspace/settings"; null outside an MCP request and when the scope has no public
    // origin.
    public static async Task<string> GetLinkAsync(HttpContext context, string route)
    {
        var scope = context.GetMcpScope();

        if (scope == null)
        {
            return null;
        }

        var origin = await context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope).GetOriginAsync(context, scope);

        return string.IsNullOrEmpty(origin) ? null : $"{origin.TrimEnd('/')}{route}";
    }

    // A screen with its link in brackets, or the screen alone when there is no link.
    public static string WithLink(string screen, string link)
    {
        return string.IsNullOrEmpty(link) ? screen : $"{screen} ({link})";
    }
}
