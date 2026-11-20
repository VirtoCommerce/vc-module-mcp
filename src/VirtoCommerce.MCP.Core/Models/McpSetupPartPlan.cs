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

    // The permissions the plan needs when they follow what it does rather than whether the object is being created: the
    // catalog part asks for catalog:read when it shows a catalog, catalog:update when it changes one and catalog:create
    // when it creates one. The setup's tools check them, with the part's ScopeType, for the requester and again at
    // approval, before the plan is shown or applied. Null: the part's Create or Update permission (McpSetupPermissions).
    public IList<string> RequiredPermissions { get; set; }

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

    // Adds a permission the plan needs (RequiredPermissions), once.
    public McpSetupPartPlan RequirePermission(string permission)
    {
        if (string.IsNullOrEmpty(permission))
        {
            return this;
        }

        RequiredPermissions ??= [];

        if (!RequiredPermissions.ContainsIgnoreCase(permission))
        {
            RequiredPermissions.Add(permission);
        }

        return this;
    }
}
