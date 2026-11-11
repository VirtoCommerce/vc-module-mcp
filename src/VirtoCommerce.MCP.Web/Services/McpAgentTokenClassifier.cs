using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Services;

// Which tokens are an MCP agent's: issued for an MCP endpoint (one of its audiences), or issued to a client application
// registered for an MCP resource, as Connected agents lists them. The client check covers a token of an agent's
// authorization requested without a resource, which carries no MCP audience. Every MCP scope counts, switched off or
// not, so the tokens already issued for a scope stay recognised when it is switched off. The client check is cached
// per client id, since it runs for every request with a token, on every endpoint.
public class McpAgentTokenClassifier(
    IEnumerable<McpScope> scopes,
    McpConnectedAgentService connectedAgentService,
    IMemoryCache memoryCache)
{
    private const string CacheKeyPrefix = "VirtoCommerce.MCP.AgentClient:";

    // How soon a client application newly registered for an MCP resource, or no longer registered, is seen as such
    protected static readonly TimeSpan ClientCacheLifetime = TimeSpan.FromMinutes(1);

    private readonly string[] _endpointPaths = scopes.Select(x => x.Path).Append(McpScope.HostEndpointPath).ToArray();

    public virtual async Task<bool> IsAgentTokenAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (McpTokenValidator.IsIssuedForMcp(principal, _endpointPaths))
        {
            return true;
        }

        foreach (var clientId in McpTokenValidator.GetClientIds(principal))
        {
            if (await IsAgentClientAsync(clientId))
            {
                return true;
            }
        }

        return false;
    }

    protected virtual Task<bool> IsAgentClientAsync(string clientId)
    {
        return memoryCache.GetOrCreateAsync(CacheKeyPrefix + clientId, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ClientCacheLifetime;
            return connectedAgentService.IsAgentClientAsync(clientId);
        });
    }
}
