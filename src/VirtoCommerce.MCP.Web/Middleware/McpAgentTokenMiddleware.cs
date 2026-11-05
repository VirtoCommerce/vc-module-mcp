using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;

namespace VirtoCommerce.MCP.Web.Middleware;

// An MCP agent's token is a platform access token, so it also reaches the REST API and GraphQL, where the platform does
// not look it up in the token store. On every other endpoint, an agent's token (McpAgentTokenClassifier) is checked
// there too, so Disconnect ends the agent's access everywhere at once: a revoked one is answered with 401 invalid_token
// (RFC 6750), as the MCP endpoints answer it. Other tokens, cookies and API keys are not touched, and are not looked up
// in the token store.
public class McpAgentTokenMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<McpOptions> options,
        McpAgentTokenClassifier agentTokenClassifier,
        IMcpTokenValidator tokenValidator)
    {
        var user = context.User;

        if (options.Value.ValidateAgentTokensOnAllEndpoints &&
            user?.Identity?.IsAuthenticated == true &&
            await agentTokenClassifier.IsAgentTokenAsync(user) &&
            !await tokenValidator.IsActiveAsync(user, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"The AI agent was disconnected. Sign in again to continue.\"";
            context.Response.Headers[HeaderNames.CacheControl] = "no-store";
            return;
        }

        await next(context);
    }
}
