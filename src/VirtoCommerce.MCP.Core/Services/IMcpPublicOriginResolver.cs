using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpPublicOriginResolver
{
    string GetOrigin(HttpContext context, McpScope scope);
}
