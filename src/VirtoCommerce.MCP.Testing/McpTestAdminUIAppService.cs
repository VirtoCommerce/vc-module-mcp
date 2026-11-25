using System.Collections.Generic;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core;

namespace VirtoCommerce.MCP.Testing;

// The admin UIs of a tool test: the AngularJS back office at the site root, as on a platform where it is installed, so
// links to its screens (IMcpLinkResolver) start with the scope's origin. A test registers its own IAdminUIAppService for
// another setup.
internal sealed class McpTestAdminUIAppService : IAdminUIAppService
{
    public IList<AdminUIApp> GetAdminUIs()
    {
        return [CreateBackOffice()];
    }

    public AdminUIApp GetRootAdminUI()
    {
        return CreateBackOffice();
    }

    private static AdminUIApp CreateBackOffice()
    {
        return new AdminUIApp { Id = McpLinkResolver.BackOfficeAppId, Path = "/apps/platform", Url = "/", IsRoot = true };
    }
}
