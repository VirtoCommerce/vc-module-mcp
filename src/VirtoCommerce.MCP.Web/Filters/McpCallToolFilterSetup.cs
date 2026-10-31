using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Services;

namespace VirtoCommerce.MCP.Web.Filters;

public class McpCallToolFilterSetup : IPostConfigureOptions<McpServerOptions>
{
    public void PostConfigure(string name, McpServerOptions options)
    {
        options.Filters.Request.CallToolFilters.Insert(0, next => (context, cancellationToken) => InvokeAsync(next, context, cancellationToken));
        options.Filters.Request.ListToolsFilters.Add(next => (context, cancellationToken) => ListToolsAsync(next, context, cancellationToken));
#pragma warning disable MCPEXP002
        options.Filters.Request.CallToolWithAlternateFilters.Insert(0, AuditRefusalAsync);
#pragma warning restore MCPEXP002
    }

    private static async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var scope = GetScope(context);
        if (scope == null)
        {
            // Endpoints that a module maps itself have no audit or approval, but the permissions of a tool still apply.
            if (context.MatchedPrimitive is McpServerTool tool &&
                !context.Services.GetRequiredService<McpToolAuthorizationService>().HasMcpPermissions(context.User, tool))
            {
                throw new McpProtocolException($"You are not allowed to run tool '{tool.ProtocolTool.Name}'.", McpErrorCode.InvalidRequest);
            }

            return await next(context, cancellationToken);
        }

        return await context.Services.GetRequiredService<McpCallToolFilter>().InvokeAsync(next, context, scope, cancellationToken);
    }

#pragma warning disable MCPEXP002
    private static async ValueTask<ResultOrAlternate<CallToolResult>> AuditRefusalAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, ResultOrAlternate<CallToolResult>> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(context, cancellationToken);
        }
        catch (McpProtocolException exception) when (!McpCallToolFilter.IsAudited(context))
        {
            var scope = GetScope(context);
            if (scope != null)
            {
                await context.Services.GetRequiredService<McpCallToolFilter>().AuditRefusalAsync(context, scope, exception);
            }

            throw;
        }
    }
#pragma warning restore MCPEXP002

    // Hides the tools whose [McpPermission] the user does not have, as the SDK does for [Authorize].
    private static async ValueTask<ListToolsResult> ListToolsAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellationToken)
    {
        var result = await next(context, cancellationToken);
        var toolCollection = context.Server.ServerOptions.ToolCollection;
        if (result?.Tools == null || toolCollection == null)
        {
            return result;
        }

        var authorizationService = context.Services.GetRequiredService<McpToolAuthorizationService>();
        var isApprover = await authorizationService.IsApproverAsync(context.User, GetScope(context));
        result.Tools = result.Tools
            .Where(tool => !toolCollection.TryGetPrimitive(tool.Name, out var primitive) ||
                (authorizationService.HasMcpPermissions(context.User, primitive) &&
                    (isApprover || !primitive.Metadata.OfType<McpApproverOnlyAttribute>().Any())))
            .ToList();

        return result;
    }

    private static McpScope GetScope(MessageContext context)
    {
        return context.Services.GetRequiredService<IHttpContextAccessor>().HttpContext.GetMcpScope();
    }
}
