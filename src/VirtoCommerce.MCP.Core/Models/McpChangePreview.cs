using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VirtoCommerce.MCP.Core.Models;

// A common shape for the result of an intent-level action: what the action does (or did) to which object, as a diff.
// Return it from a tool with a dryRun parameter, so the approver of a pending change and the agent see the same preview.
public class McpChangePreview
{
    [JsonPropertyName("action")]
    public string Action { get; set; }

    [JsonPropertyName("object_type")]
    public string ObjectType { get; set; }

    [JsonPropertyName("object_id")]
    public string ObjectId { get; set; }

    [JsonPropertyName("object_name")]
    public string ObjectName { get; set; }

    // True when nothing was changed: the tool ran with dryRun.
    [JsonPropertyName("dry_run")]
    public bool DryRun { get; set; }

    [JsonPropertyName("changes")]
    public IList<McpFieldChange> Changes { get; set; } = [];

    // Things the approver should know before the change is applied, for example an overlap with another promotion.
    [JsonPropertyName("warnings")]
    public IList<string> Warnings { get; set; } = [];

    public McpChangePreview AddChange(string field, object from, object to)
    {
        if (!Equals(from, to))
        {
            Changes.Add(new McpFieldChange { Field = field, From = from, To = to });
        }

        return this;
    }

    public McpChangePreview AddWarning(string warning)
    {
        if (!string.IsNullOrEmpty(warning))
        {
            Warnings.Add(warning);
        }

        return this;
    }
}

public class McpFieldChange
{
    [JsonPropertyName("field")]
    public string Field { get; set; }

    [JsonPropertyName("from")]
    public object From { get; set; }

    [JsonPropertyName("to")]
    public object To { get; set; }
}
