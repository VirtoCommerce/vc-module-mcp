using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Services;

// The routes of GetLinkAsync are those of the AngularJS back office, the admin UI app "platform"; GetAppLinkAsync links
// any admin UI app. A link follows where the platform serves the app: at the site root when the app owns it
// (VirtoCommerce:PlatformUI:RootApp), at /apps/{appId}/ otherwise.
public class McpLinkResolver(
    IHttpContextAccessor httpContextAccessor,
    IAdminUIAppService adminUIAppService,
    IOptionsMonitor<PlatformUIOptions> platformUIOptions)
    : IMcpLinkResolver
{
    public const string BackOfficeAppId = "platform";

    public virtual Task<string> GetLinkAsync(string route)
    {
        return GetAppLinkAsync(BackOfficeAppId, route);
    }

    public virtual async Task<string> GetAppLinkAsync(string appId, string route)
    {
        var context = httpContextAccessor.HttpContext;
        var scope = context.GetMcpScope();
        var app = GetAdminUI(appId);

        if (scope == null || app == null)
        {
            return null;
        }

        var origin = await context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope).GetOriginAsync(context, scope);

        return string.IsNullOrEmpty(origin) ? null : $"{origin.TrimEnd('/')}{app.Url?.TrimEnd('/')}{route}";
    }

    // The admin UI app as the platform serves it (Url is "/" at the site root, "/apps/{appId}/" otherwise); null when it is
    // not installed. With the platform's admin UI switched off (VirtoCommerce:PlatformUI:Enable), the back office is null
    // too: the AngularJS admin then refuses its page, also at /apps/platform/, while the platform still serves the other
    // admin UI apps, such as Admin Shell, at /apps/{appId}/.
    protected virtual AdminUIApp GetAdminUI(string appId)
    {
        if (!platformUIOptions.CurrentValue.Enable && appId.EqualsIgnoreCase(BackOfficeAppId))
        {
            return null;
        }

        return adminUIAppService.GetAdminUIs().FirstOrDefault(x => x.Id.EqualsIgnoreCase(appId));
    }
}
