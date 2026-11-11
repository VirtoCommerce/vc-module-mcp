using ModelContextProtocol;

namespace VirtoCommerce.MCP.Core;

// Thrown by a tool when a resource-level check fails inside the tool, for example a promotion of a store the user
// may not manage, or a row of another seller. The agent gets the message as a tool error, and the call is audited as Forbidden.
public class McpForbiddenException(string message) : McpException(message)
{
}
