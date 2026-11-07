using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtoCommerce.MCP.Web.Services;

// Reads the preview and the result of a pending change for a reviewer, in the approval tools and in the back office.
public static class McpToolResultJson
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    public static JsonNode Parse(string json)
    {
        return string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json);
    }

    // The preview and the result are stored as the tool returned them, a CallToolResult. The reviewer reads what the tool
    // said: its structured content, or the JSON or the text of its content, with is_error when the tool failed.
    public static JsonNode Unwrap(string json)
    {
        var node = Parse(json);
        if (node is not JsonObject result || !result.ContainsKey("content"))
        {
            return node;
        }

        var value = result["structuredContent"]?.DeepClone() ?? GetContentValue(result["content"] as JsonArray);
        if (result["isError"]?.GetValueKind() != JsonValueKind.True)
        {
            return value;
        }

        return new JsonObject { ["is_error"] = true, ["message"] = value };
    }

    public static string ToIndentedString(JsonNode node)
    {
        return node?.ToJsonString(_indented);
    }

    private static JsonNode GetContentValue(JsonArray content)
    {
        var texts = content?
            .OfType<JsonObject>()
            .Where(x => x["type"]?.GetValue<string>() == "text")
            .Select(x => x["text"]?.GetValue<string>())
            .ToList() ?? [];

        if (texts.Count != 1)
        {
            return content?.DeepClone();
        }

        try
        {
            return JsonNode.Parse(texts[0]);
        }
        catch (JsonException)
        {
            return JsonValue.Create(texts[0]);
        }
    }
}
