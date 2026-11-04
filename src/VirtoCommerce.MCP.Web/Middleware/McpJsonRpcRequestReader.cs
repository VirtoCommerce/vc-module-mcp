using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace VirtoCommerce.MCP.Web.Middleware;

// Reads the method and the tool name of a single JSON-RPC request without consuming the body, so the MCP server still reads it.
public static class McpJsonRpcRequestReader
{
    public static async Task<(string Method, string ToolName)> ReadAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method) || !request.HasJsonContentType())
        {
            return (null, null);
        }

        request.EnableBuffering();

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            {
                return (null, null);
            }

            return (method.GetString(), GetToolName(root));
        }
        catch (JsonException)
        {
            return (null, null);
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    // params.name of a tools/call request. Matched ignoring case, as the MCP server reads the parameters.
    private static string GetToolName(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetPropertyIgnoreCase(parameters, "name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        var match = element.EnumerateObject()
            .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(x => (JsonElement?)x.Value)
            .FirstOrDefault();

        value = match ?? default;
        return match.HasValue;
    }
}
