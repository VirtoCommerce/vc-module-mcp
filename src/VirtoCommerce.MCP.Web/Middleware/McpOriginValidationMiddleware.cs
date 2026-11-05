using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Web.Middleware;

// Refuses an MCP request from a browser page of an origin the scope does not allow (IMcpOriginValidator), with 403, as
// the MCP transport requires against DNS rebinding. A request without an Origin header, which every server-side MCP
// client sends, is not checked. It runs after the platform's authentication, so the modules' steps after routing, such
// as UCP's store of the request, have run and the scope's origin is the one its tokens are checked against.
public class McpOriginValidationMiddleware(
    RequestDelegate next,
    ILogger<McpOriginValidationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var origins = context.Request.Headers.Origin;
        if (StringValues.IsNullOrEmpty(origins))
        {
            await next(context);
            return;
        }

        var scope = context.GetMcpScope();
        var originValidator = context.RequestServices.GetMcpScopeService<IMcpOriginValidator>(scope);

        // A browser sends one Origin; more than one is not a browser's request and is refused.
        if (origins.Count == 1 && await originValidator.IsAllowedAsync(context, scope, origins.ToString()))
        {
            await next(context);
            return;
        }

        logger.LogWarning("Refused a request to the MCP endpoint of scope {Scope} from origin {Origin}", scope?.Name, origins.ToString());

        var challengeWriter = context.RequestServices.GetMcpScopeService<McpChallengeWriter>(scope);
        await challengeWriter.WriteForbiddenAsync(context, scope, "Requests from this origin are not allowed to this MCP server.");
    }
}
