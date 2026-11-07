using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace VirtoCommerce.MCP.Core.Services;

// The rules an MCP access token must meet, for the MCP endpoints and for any other endpoint that accepts the same tokens
// (such as a REST API of the same protocol): the bearer token as OpenIddict reads it, issued for the resource, to a
// client, for a user, and still active in the token store.
public interface IMcpTokenValidator
{
    // The user of the request's bearer token as the platform's OpenIddict validation reads it, never a cookie, an API key
    // or Basic credentials; null when the token is not valid.
    Task<ClaimsPrincipal> AuthenticateAsync(HttpContext context);

    // Whether the token is still active in the token store: not revoked, and its authorization not revoked either.
    Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    // Why the token of the principal must not be accepted for the resource (an absolute URL such as
    // https://shop.example.com/ucp/mcp), or null when it is accepted.
    Task<string> GetTokenErrorAsync(ClaimsPrincipal principal, string expectedResource, CancellationToken cancellationToken);
}
