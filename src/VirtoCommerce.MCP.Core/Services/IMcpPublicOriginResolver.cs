using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpPublicOriginResolver
{
    Task<string> GetOriginAsync(HttpContext context, McpScope scope);
}
