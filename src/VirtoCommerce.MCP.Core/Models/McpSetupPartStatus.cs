namespace VirtoCommerce.MCP.Core.Models;

public enum McpSetupPartStatus
{
    NoChange,
    WillChange,
    Applied,
    Failed,
    // Not started: the setup stopped before this part.
    NotApplied,
}
