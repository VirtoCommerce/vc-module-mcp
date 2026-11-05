using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Middleware;

// Caps the body of an MCP request before anything reads it. The scope's middleware reads the JSON-RPC request ahead of
// authentication, and the platform sets no limit of its own, so an anonymous request could otherwise make the server
// buffer and parse a body of any size.
public class McpRequestSizeLimitMiddleware(RequestDelegate next, IOptions<McpOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = options.Value.MaxRequestBodySize;
        if (limit <= 0)
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength > limit)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        // A body without a length (chunked) is cut off by the server once it passes the limit: the reader gets a
        // BadHttpRequestException, answered here with 413 rather than by the platform's exception handler.
        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = limit;
        }

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        }
    }
}
