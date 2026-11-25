using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Web.Services;

// The Admin Shell, the admin UI app "admin-shell" that holds this module's screens: Agent approvals is there, under
// Configuration > AI agents (MCP). The link is the app's root: the platform does not serve the app's screens at addresses
// of their own.
public class McpAdminShellLinks(IMcpLinkResolver linkResolver)
{
    public const string AdminShellAppId = "admin-shell";

    // The menu path of the screen where an approver reviews the changes that wait for approval
    public const string ApprovalsScreen = "Agent approvals: Admin Shell > Configuration > AI agents (MCP) > Agent approvals";

    // The Admin Shell at the origin of the current MCP request; null when IMcpLinkResolver gives no link.
    public virtual Task<string> GetLinkAsync()
    {
        return linkResolver.GetAppLinkAsync(AdminShellAppId, "/");
    }
}
