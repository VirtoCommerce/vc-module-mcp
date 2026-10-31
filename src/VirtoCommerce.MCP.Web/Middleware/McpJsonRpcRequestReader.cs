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

    // params.name of a tools/call request
    private static string GetToolName(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return parameters.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
    }
}
