using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace VirtoCommerce.MCP.Web.Middleware;

// Some read-only MCP probes (registry crawlers, health checks) send tools/list with "Accept: application/json" only,
// while the SDK requires text/event-stream as well and answers them with 406. For tools/list alone, the middleware
// adds text/event-stream to the request and returns the SDK's single event as plain JSON. Other requests are untouched.
public class McpProbeCompatibilityMiddleware(
    RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsJsonOnlyPost(context.Request) || !await IsToolsListRequest(context.Request))
        {
            await next(context);
            return;
        }

        context.Request.Headers.Append(HeaderNames.Accept, "text/event-stream");
        await RewriteEventStreamResponseAsJson(context);
    }

    private static bool IsJsonOnlyPost(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method) || !request.HasJsonContentType())
        {
            return false;
        }

        var acceptedMediaTypes = request.GetTypedHeaders().Accept;
        var acceptsJson = acceptedMediaTypes?.Any(x => MediaTypeEquals(x.MediaType.Value, "application/json")) == true;
        var acceptsEventStream = acceptedMediaTypes?.Any(x => MediaTypeEquals(x.MediaType.Value, "text/event-stream")) == true;

        return acceptsJson && !acceptsEventStream;
    }

    private static async Task<bool> IsToolsListRequest(HttpRequest request)
    {
        var (method, _) = await McpJsonRpcRequestReader.ReadAsync(request);
        return method == "tools/list";
    }

    private async Task RewriteEventStreamResponseAsJson(HttpContext context)
    {
        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        var rewriteResponse = false;

        context.Response.Body = responseBuffer;
        context.Response.OnStarting(() =>
        {
            rewriteResponse = IsEventStream(context.Response.ContentType);
            if (rewriteResponse)
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength = null;
            }

            return Task.CompletedTask;
        });

        try
        {
            await next(context);

            if (!context.Response.HasStarted && IsEventStream(context.Response.ContentType))
            {
                rewriteResponse = true;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength = null;
            }

            responseBuffer.Position = 0;
            context.Response.Body = originalBody;

            if (rewriteResponse)
            {
                using var reader = new StreamReader(responseBuffer, Encoding.UTF8);
                var eventStream = await reader.ReadToEndAsync(context.RequestAborted);
                await context.Response.WriteAsync(ExtractEventStreamData(eventStream), context.RequestAborted);
            }
            else
            {
                await responseBuffer.CopyToAsync(originalBody, context.RequestAborted);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static string ExtractEventStreamData(string eventStream)
    {
        var data = eventStream
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].TrimStart())
            .ToList();

        // The data lines of one event are joined with a line feed, as the event stream format defines.
        return data.Count > 0 ? string.Join('\n', data) : eventStream;
    }

    private static bool IsEventStream(string contentType)
    {
        return contentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool MediaTypeEquals(string value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
