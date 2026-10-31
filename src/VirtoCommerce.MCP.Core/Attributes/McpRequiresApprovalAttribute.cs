using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// The call is held as a pending change until another person approves it.
//
// Condition names a public static method of the tool class that decides from the arguments whether this call needs
// approval, for example only above a number of coupons, or only when content goes live today. The method takes the
// call's arguments as IReadOnlyDictionary<string, JsonElement> and returns bool; generate_coupons of the marketing
// sample uses Condition = nameof(NeedsApproval) to hold only calls for more than 1,000 coupons.
//
// Without a condition, every call needs approval.
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpRequiresApprovalAttribute : Attribute
{
    public string Condition { get; set; }
}
