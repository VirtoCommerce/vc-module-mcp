using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace VirtoCommerce.MCP.Core;

// The JSON of every tool, prompt and resource the MCP module serves, whichever module registers it: the MCP SDK's
// defaults with snake_case names, the convention of MCP tool results. One set for all modules, so an agent sees the same
// conventions in every tool, and a module does not pass its own. Read-only: a module cannot change it for the others.
// An agent reads the JSON as text, so characters are written as they are ('>', '&', "Müller"), not as \u escapes, which
// read badly and cost tokens; the JSON is not put into an HTML page as it is.
public static class McpSerialization
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.MakeReadOnly();

        return options;
    }
}
