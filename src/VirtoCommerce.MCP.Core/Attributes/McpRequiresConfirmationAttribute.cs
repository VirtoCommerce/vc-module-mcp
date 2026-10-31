using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// The user confirms the call in the chat before the tool runs (MCP elicitation). The agent's client shows the message
// with Confirm and Decline; a declined call does not run and is audited as Declined. The client must support
// elicitation in the 2026-07-28 protocol revision (multi round-trip requests); with other clients the call runs as
// without this attribute, and they ask the user with their own tool approval prompt.
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
}
