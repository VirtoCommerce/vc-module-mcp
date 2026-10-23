using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace VirtoCommerce.MCP.Web.Middleware;

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
        request.EnableBuffering();

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("method", out var method) &&
                method.ValueKind == JsonValueKind.String &&
                method.ValueEquals("tools/list");
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            request.Body.Position = 0;
        }
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
        return eventStream
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].TrimStart())
            .Single();
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
