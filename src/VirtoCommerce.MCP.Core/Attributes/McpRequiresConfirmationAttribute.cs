using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// The user confirms the call in the chat before the tool runs (MCP elicitation). The agent's client shows the message
// with Confirm and Decline; a declined call does not run and is audited as Declined. The client must support
// elicitation in the 2026-07-28 protocol revision (multi round-trip requests). What happens with other clients is the
// tool's Fallback.
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpRequiresConfirmationAttribute : Attribute
{
    public McpRequiresConfirmationAttribute(string message = null)
    {
        Message = message;
    }

    // The question shown to the user. {argument} is replaced by the value of that argument of the call, so the user
    // sees what will change: "Pause promotion {promotionId}?". Without it, the tool's title and the arguments are shown.
    public string Message { get; }

    // What happens when the client cannot ask the user in the chat.
    public McpConfirmationFallback Fallback { get; set; } = McpConfirmationFallback.Allow;
}

public enum McpConfirmationFallback
{
    // The call runs as without the attribute; the client's own tool approval prompt is the only confirmation.
    Allow,

    // The call does not run and is audited as Declined: the user does it where a person confirms it, such as the back
    // office, or with a client that can ask. For calls that must never run on the agent's word alone.
    Refuse,
}
