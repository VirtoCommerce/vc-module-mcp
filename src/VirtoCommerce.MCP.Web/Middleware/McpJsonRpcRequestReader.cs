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

            var toolName = root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object &&
                parameters.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;

            return (method.GetString(), toolName);
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
}
