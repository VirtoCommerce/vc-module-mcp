using System;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using VirtoCommerce.MCP.Core;

namespace VirtoCommerce.MCP.Testing;

public enum McpToolCallOutcome
{
    // The tool ran and answered.
    Succeeded,

    // The tool requires approval for these arguments: it ran as a dry run, and Json is the preview an approver sees.
    PendingApproval,

    // The tool asks for confirmation, and the test declined it: the tool did not run.
    Declined,

    // The tool refused the call with McpForbiddenException, such as for a store the user does not manage.
    Forbidden,

    // The tool failed, or answered with an error.
    Failed,

    // The user may not see the tool: a permission is missing, or the tool is switched off.
    NotAvailable,

    // No tool of that name in the scope.
    NotFound,
}

// What a tool call through McpToolHarness ended with, as the agent would see it.
public sealed class McpToolCallResult
{
    public string ToolName { get; init; }

    public McpToolCallOutcome Outcome { get; init; }

    // The SDK's result of the call; null when the tool did not run.
    public CallToolResult Result { get; init; }

    // The message of a refusal or a failure.
    public string Error { get; init; }

    // The tool asks its user to confirm in the chat ([McpRequiresConfirmation]); the harness answered.
    public bool AskedForConfirmation { get; init; }

    // The tool ran as a dry run: the test asked for it, or the call requires approval.
    public bool WasDryRun { get; init; }

    public bool Succeeded => Outcome == McpToolCallOutcome.Succeeded;

    // The text blocks of the result, joined.
    public string Text => Result == null ? null : string.Join(Environment.NewLine, Result.Content.OfType<TextContentBlock>().Select(x => x.Text));

    // The answer as JSON: the structured content, or the text when it is JSON.
    public JsonElement Json
    {
        get
        {
            if (Result?.StructuredContent is { } structured)
            {
                return structured;
            }

            var text = Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return default;
            }

            try
            {
                return JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException)
            {
                return JsonSerializer.SerializeToElement(text);
            }
        }
    }

    // The answer read into a type, with the names the agent sees (McpSerialization.Options: snake_case).
    public T Read<T>()
    {
        var json = Json;
        return json.ValueKind == JsonValueKind.Undefined ? default : json.Deserialize<T>(McpSerialization.Options);
    }

    // Throws with the outcome and the message unless the call succeeded; returns the result for chaining.
    public McpToolCallResult EnsureSucceeded()
    {
        return EnsureOutcome(McpToolCallOutcome.Succeeded);
    }

    public McpToolCallResult EnsureOutcome(McpToolCallOutcome expected)
    {
        if (Outcome != expected)
        {
            throw new InvalidOperationException($"Tool '{ToolName}' ended with {Outcome}, not {expected}. {Error ?? Text}".TrimEnd());
        }

        return this;
    }

    public override string ToString()
    {
        return $"{ToolName}: {Outcome}{(string.IsNullOrEmpty(Error) ? "" : $" ({Error})")}";
    }
}
