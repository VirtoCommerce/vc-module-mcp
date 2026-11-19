using System.Threading.Tasks;

namespace VirtoCommerce.MCP.Core.Services;

// Links to the screens of the classic back office, for what the tools do not cover: the public origin of the request's
// scope (IMcpPublicOriginResolver), where the platform serves the back office, and the screen's route. A solution
// replaces it to send the links elsewhere.
public interface IMcpLinkResolver
{
    // The link to a route of the classic back office, such as "/#!/workspace/store"; null outside an MCP request, when the
    // scope has no public origin, and when the back office is not installed or the platform's admin UI is switched off.
    Task<string> GetLinkAsync(string route);
}
