using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Filters;

public class McpCallToolFilterSetup : IPostConfigureOptions<McpServerOptions>
{
    public void PostConfigure(string name, McpServerOptions options)
    {
        options.Filters.Request.CallToolFilters.Insert(0, next => (context, cancellationToken) => InvokeAsync(next, context, cancellationToken));
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

    private static McpScope GetScope(RequestContext<CallToolRequestParams> context)
    {
        return context.Services.GetRequiredService<IHttpContextAccessor>().HttpContext.GetMcpScope();
    }
}
