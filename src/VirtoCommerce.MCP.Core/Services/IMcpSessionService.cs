using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpSessionService
{
    // Whether the access token of the principal is still valid in the platform token store.
    Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    // Ends the OAuth sign-in of the access token: the access and refresh tokens of the grant stop working on all replicas,
    // and the MCP client has to sign the user in again. For a sign_out or logout tool.
    Task SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
