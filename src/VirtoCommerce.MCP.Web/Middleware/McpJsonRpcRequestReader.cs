using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Middleware;

// Reads the method and the tool names of a single JSON-RPC request without consuming the body, so the MCP server still reads it.
public static class McpJsonRpcRequestReader
{
    // The request as read for it once (HttpContext.GetMcpRequest), or read now and kept for the rest of the pipeline.
    public static async Task<McpRequestInfo> GetOrReadAsync(HttpContext context)
    {
        var request = context.GetMcpRequest();
        if (request == null)
        {
            request = await ReadAsync(context.Request);
            context.SetMcpRequest(request);
        }

        return request;
    }

    public static async Task<McpRequestInfo> ReadAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method) || !request.HasJsonContentType())
        {
            return McpRequestInfo.None;
        }

        request.EnableBuffering();

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            {
                return McpRequestInfo.None;
            }

            return new McpRequestInfo(method.GetString(), GetToolNames(root));
        }
        catch (JsonException)
        {
            return McpRequestInfo.None;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    // params.name of a tools/call request. Matched ignoring case, as the MCP server reads the parameters, and every
    // match is kept: which one the server binds is not for this reader to guess.
    private static IEnumerable<string> GetToolNames(JsonElement root)
    {
        return GetPropertiesIgnoreCase(root, "params")
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .SelectMany(x => GetPropertiesIgnoreCase(x, "name"))
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .ToList();
    }

    private static IEnumerable<JsonElement> GetPropertiesIgnoreCase(JsonElement element, string name)
    {
        return element.EnumerateObject()
            .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value);
    }
}
