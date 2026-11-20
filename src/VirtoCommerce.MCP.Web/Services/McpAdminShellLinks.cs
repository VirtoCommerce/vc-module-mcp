using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Web.Services;

// The Admin Shell, the admin UI app "admin-shell" that holds this module's screens: Agent approvals is there, under
// Configuration > AI agents (MCP). The link is the app's root, where the platform serves it (Url is "/" when it owns the
// site root, "/apps/admin-shell/" otherwise): the platform does not serve the app's screens at their own addresses.
public class McpAdminShellLinks(
    IHttpContextAccessor httpContextAccessor,
    IAdminUIAppService adminUIAppService,
    IOptionsMonitor<PlatformUIOptions> platformUIOptions)
{
    public const string AdminShellAppId = "admin-shell";

    // The menu path of the screen where an approver reviews the changes that wait for approval
    public const string ApprovalsScreen = "Agent approvals: Admin Shell > Configuration > AI agents (MCP) > Agent approvals";

    // The Admin Shell at the origin of the current MCP request; null outside an MCP request, when the Admin Shell is not
    // installed, or when the platform's admin UI is switched off (VirtoCommerce:PlatformUI:Enable).
    public virtual async Task<string> GetLinkAsync()
    {
        var context = httpContextAccessor.HttpContext;
        var scope = context.GetMcpScope();
        var adminShell = GetAdminShell();

        if (scope == null || adminShell == null)
        {
            return null;
        }

        var origin = await context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope).GetOriginAsync(context, scope);

        return string.IsNullOrEmpty(origin) ? null : $"{origin.TrimEnd('/')}{adminShell.Url}";
    }

    protected virtual AdminUIApp GetAdminShell()
    {
        if (!platformUIOptions.CurrentValue.Enable)
        {
            return null;
        }

        return adminUIAppService.GetAdminUIs().FirstOrDefault(x => x.Id.EqualsIgnoreCase(AdminShellAppId));
    }
}
