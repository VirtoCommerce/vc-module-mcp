using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.Platform.Core;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.MCP.Core.Services;

// The routes are those of the AngularJS back office, the admin UI app "platform", and the link follows where the platform
// serves it: at the site root, or at /apps/platform/ when another admin UI owns the root (VirtoCommerce:PlatformUI:RootApp).
public class McpLinkResolver(
    IHttpContextAccessor httpContextAccessor,
    IAdminUIAppService adminUIAppService,
    IOptionsMonitor<PlatformUIOptions> platformUIOptions)
    : IMcpLinkResolver
{
    public const string BackOfficeAppId = "platform";

    public virtual async Task<string> GetLinkAsync(string route)
    {
        var context = httpContextAccessor.HttpContext;
        var scope = context.GetMcpScope();
        var backOffice = GetBackOffice();

        if (scope == null || backOffice == null)
        {
            return null;
        }

        var origin = await context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope).GetOriginAsync(context, scope);

        return string.IsNullOrEmpty(origin) ? null : $"{origin.TrimEnd('/')}{backOffice.Url?.TrimEnd('/')}{route}";
    }

    // The AngularJS back office as the platform serves it (Url is "/" at the site root, "/apps/platform/" otherwise); null
    // when it is not installed, or when the platform's admin UI is switched off (VirtoCommerce:PlatformUI:Enable), as the
    // back office then refuses its page.
    protected virtual AdminUIApp GetBackOffice()
    {
        if (!platformUIOptions.CurrentValue.Enable)
        {
            return null;
        }

        return adminUIAppService.GetAdminUIs().FirstOrDefault(x => x.Id.EqualsIgnoreCase(BackOfficeAppId));
    }
}
