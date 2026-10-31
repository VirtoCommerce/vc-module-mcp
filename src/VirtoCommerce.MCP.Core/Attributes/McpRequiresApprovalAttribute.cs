using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// The call is held as a pending change until another person approves it.
//
// Condition names a public static method of the tool class that decides from the arguments whether this call needs
// approval, for example only above a number of coupons, or only when content goes live today:
//
//     [McpRequiresApproval(Condition = nameof(NeedsApproval))]
//     public object GenerateCoupons(int count, ...) { ... }
//
//     public static bool NeedsApproval(IReadOnlyDictionary<string, JsonElement> arguments) =>
//         arguments.TryGetValue("count", out var count) && count.GetInt32() > 1000;
//
// Without a condition, every call needs approval.
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpRequiresApprovalAttribute : Attribute
{
    public string Condition { get; set; }
}
