using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Services;

public class McpSessionService(
    McpTokenValidator tokenValidator,
    IOpenIddictTokenManager tokenManager,
    IUserSessionsService userSessionsService)
    : IMcpSessionService
{
    public virtual Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        return tokenValidator.IsActiveAsync(principal, cancellationToken);
    }

    public virtual async Task SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var authorizationId = principal?.GetAuthorizationId();
        var tokenId = principal?.GetTokenId();

        if (string.IsNullOrEmpty(authorizationId) && !string.IsNullOrEmpty(tokenId))
        {
            var token = await tokenManager.FindByIdAsync(tokenId, cancellationToken);
            authorizationId = token != null ? await tokenManager.GetAuthorizationIdAsync(token, cancellationToken) : null;
        }

        if (!string.IsNullOrEmpty(authorizationId))
        {
            await userSessionsService.TerminateUserSessionGroup(authorizationId);
        }
        else if (!string.IsNullOrEmpty(tokenId))
        {
            await userSessionsService.TerminateUserSession(tokenId);
        }
    }
}
