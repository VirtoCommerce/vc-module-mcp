using System.Threading.Tasks;

namespace VirtoCommerce.MCP.Core.Services;

// Links to the screens of the platform's UI, for what the tools do not cover: the public origin of the request's scope
// (IMcpPublicOriginResolver) followed by the screen's route. A solution replaces it to send the links elsewhere.
public interface IMcpLinkResolver
{
    // The link to a route of the platform's UI, such as "/#!/workspace/store"; null outside an MCP request and when the
    // scope has no public origin.
    Task<string> GetLinkAsync(string route);
}
