using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Models;

// What applying a part's section of a setup would change. A part derives it to carry the state after the change for its
// check.
public class McpSetupPartPlan
{
    public IList<McpFieldChange> Changes { get; set; } = [];

    // Why the section cannot be applied.
    public IList<string> Errors { get; set; } = [];

    // What the approver should know, such as a method being switched off.
    public IList<string> Warnings { get; set; } = [];

    // Records a change, unless the two values are equal (object.Equals), as McpChangePreview.AddChange does. A part
    // compares its values as its rules say before it calls this; a list is recorded whenever it is a new list.
    public McpSetupPartPlan AddChange(string field, object from, object to)
    {
        if (Equals(from, to))
        {
            return this;
        }

        var change = AbstractTypeFactory<McpFieldChange>.TryCreateInstance();
        change.Field = field;
        change.From = from;
        change.To = to;
        Changes.Add(change);

        return this;
    }
}
