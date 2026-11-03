using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpPublicOriginResolver
{
    Task<string> GetOriginAsync(HttpContext context, McpScope scope);

    // Whether the origin comes from the configuration rather than from the request's Host and forwarded headers: a
    // configured origin (MCP:PublicOrigin or the scope's), or a host the scope is configured to serve /mcp on.
    bool IsOriginConfigured(HttpContext context, McpScope scope);
}
