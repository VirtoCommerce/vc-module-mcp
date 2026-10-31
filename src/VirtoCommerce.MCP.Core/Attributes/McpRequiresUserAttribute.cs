using System;

namespace VirtoCommerce.MCP.Core.Attributes;

// For a scope that allows anonymous users: the tool is listed to everyone, and a call without a token is answered with
// an HTTP 401 OAuth challenge, so the MCP client signs the user in and repeats the call. Tools with [Authorize] are
// challenged the same way, but they are not listed to anonymous users.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class McpRequiresUserAttribute : Attribute
{
}
