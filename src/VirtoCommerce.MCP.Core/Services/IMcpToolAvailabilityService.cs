using System.Collections.Generic;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

// The tool switches of Agent tools: which tools agents may use now. The context carries the user and the request, so a
// replacement can switch a tool off for one store, organization or user, not only for a whole scope.
public interface IMcpToolAvailabilityService
{
    // The tools switched off in the context's scope, with "*" when the whole scope is; the context's tool is not used.
    Task<IReadOnlySet<string>> GetDisabledToolNamesAsync(McpToolAvailabilityContext context);

    Task<bool> IsDisabledAsync(McpToolAvailabilityContext context);

    // Switches a tool, or with * a whole scope, on or off in one scope.
    Task SetEnabledAsync(string scopeName, string toolName, bool enabled, IEnumerable<string> allScopeNames);
}
