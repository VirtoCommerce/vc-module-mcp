using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.Core.Models;

// The JSON-RPC request that came to an MCP endpoint: its method and, for tools/call, the tool it names. Read once, ahead
// of authentication (HttpContext.GetMcpRequest), so a policy of the platform pipeline sees the operation without
// parsing the body again. The MCP server reads the parameters ignoring case, so a request can name several tools
// ("name" and "Name"); such a request is ambiguous, and a policy decides on every name it carries.
public sealed class McpRequestInfo
{
    public static McpRequestInfo None { get; } = new(null, []);

    public McpRequestInfo(string method, IEnumerable<string> toolNames)
    {
        Method = method;
        ToolNames = (toolNames ?? []).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToArray();
    }

    // The JSON-RPC method, such as tools/call or tools/list; null when the body is not a single JSON-RPC request
    public string Method { get; }

    // Every tool name the request carries in params.name, matched ignoring case
    public IReadOnlyList<string> ToolNames { get; }

    // The tool of a tools/call request, or null when it names none or several
    public string ToolName => ToolNames.Count == 1 ? ToolNames[0] : null;

    public bool IsAmbiguous => ToolNames.Count > 1;

    public bool IsToolCall => Method == "tools/call";
}
