using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// A tool that needs no permission of its own: every signed-in user of the scope may see and call it, and the tool
// checks access to its data itself. In a scope that requires a signed-in user, a tool must declare who may use it:
// [Authorize], [McpPermission], [AllowAnonymous] or this attribute. A tool that declares none of them is not
// served there, so a forgotten permission hides the tool instead of opening it to every user.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class McpAnyUserAttribute : Attribute
{
}
