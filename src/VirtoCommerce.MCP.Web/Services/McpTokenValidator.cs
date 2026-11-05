using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Security.Model.OpenIddict;

namespace VirtoCommerce.MCP.Web.Services;

public class McpTokenValidator(
    IOpenIddictTokenManager tokenManager,
    IOptions<OpenIddictServerOptions> serverOptions = null)
    : IMcpTokenValidator
{
    // The bearer token as the platform's OpenIddict validation reads it, not whatever the default authentication scheme
    // produced: a cookie, an API key or Basic credentials never stand in for an MCP access token. OpenIddict caches
    // the result for the request, so the token is not validated twice. Null when the token is not valid.
    public virtual async Task<ClaimsPrincipal> AuthenticateAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        return result.Succeeded ? result.Principal : null;
    }

    private const string AudienceClaimType = "aud";

    private static readonly string[] _subjectClaimTypes = [OpenIddictConstants.Claims.Subject, ClaimTypes.NameIdentifier];
    private static readonly string[] _clientClaimTypes = [OpenIddictConstants.Claims.ClientId, OpenIddictConstants.Claims.AuthorizedParty, "oi_prst"];

    // A client-credentials token: the client acts for itself, with no user behind it. The platform issues it with the
    // client id as the subject and the Administrator role, so it must not reach tools that act as the signed-in user.
    public static bool IsClientIdentity(ClaimsPrincipal principal)
    {
        if (principal == null)
        {
            return true;
        }

        var subject = principal.Claims.FirstOrDefault(claim => _subjectClaimTypes.Contains(claim.Type))?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return true;
        }

        return principal.Claims.Any(claim => _clientClaimTypes.Contains(claim.Type) && string.Equals(claim.Value, subject, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasExpectedAudience(ClaimsPrincipal principal, string expectedResource)
    {
        if (!Uri.TryCreate(expectedResource, UriKind.Absolute, out var expected))
        {
            return false;
        }

        return GetAudiences(principal).Any(value => IsExpectedAudience(value, expected));
    }

    // Whether the token was issued for an MCP endpoint: one of its audiences is an address with one of the paths.
    public static bool IsIssuedForMcp(ClaimsPrincipal principal, IEnumerable<string> endpointPaths)
    {
        var paths = endpointPaths.Select(x => x.TrimEnd('/')).ToList();

        return GetAudiences(principal).Any(value =>
            Uri.TryCreate(value, UriKind.Absolute, out var audience) &&
            paths.Contains(audience.AbsolutePath.TrimEnd('/'), StringComparer.OrdinalIgnoreCase));
    }

    // The client application the token was issued to, as the token names it
    public static IEnumerable<string> GetClientIds(ClaimsPrincipal principal)
    {
        return principal?.Claims
            .Where(claim => _clientClaimTypes.Contains(claim.Type) && !string.IsNullOrEmpty(claim.Value))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal) ?? [];
    }

    public virtual async Task<string> GetTokenErrorAsync(ClaimsPrincipal principal, string expectedResource, CancellationToken cancellationToken)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return "The access token is invalid or expired.";
        }

        if (!HasExpectedAudience(principal, expectedResource))
        {
            return "The access token was not issued for this MCP resource.";
        }

        if (IsClientIdentity(principal))
        {
            return "The access token identifies the MCP client, not a user. Connect the client with a user's sign-in.";
        }

        // A token issued with no client, such as by the password grant of an anonymous client, skips the user's consent
        // and is not listed in Connected agents, so the user could not disconnect it.
        if (!GetClientIds(principal).Any())
        {
            return "The access token was not issued to an MCP client. Connect the MCP client with a user's sign-in.";
        }

        return await IsActiveAsync(principal, cancellationToken)
            ? null
            : "The OAuth session has ended. Sign in again to continue.";
    }

    public virtual async Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var tokenId = principal.GetTokenId();
        if (string.IsNullOrEmpty(tokenId))
        {
            // Only a server without token storage issues tokens without an id; such a token cannot be revoked and stays
            // valid until it expires. With token storage, which the platform keeps on, a token without an id is refused.
            return serverOptions?.Value.DisableTokenStorage == true;
        }

        return await IsValidInTokenStoreAsync(tokenId, cancellationToken);
    }

    private ValueTask<bool> IsValidInTokenStoreAsync(string tokenId, CancellationToken cancellationToken)
    {
        return tokenManager.GetAsync(query => query
            .Cast<VirtoOpenIddictEntityFrameworkCoreToken>()
            .Where(token => token.Id == tokenId && token.Status == OpenIddictConstants.Statuses.Valid &&
                (token.Authorization == null || token.Authorization.Status == OpenIddictConstants.Statuses.Valid))
            .Select(token => true), cancellationToken);
    }

    private static IEnumerable<string> GetAudiences(ClaimsPrincipal principal)
    {
        return principal?.Claims
            .Where(claim => string.Equals(claim.Type, AudienceClaimType, StringComparison.Ordinal))
            .Select(claim => claim.Value) ?? [];
    }

    private static bool IsExpectedAudience(string value, Uri expected)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var audience) &&
            HasSameOrigin(audience, expected) &&
            HasSamePath(audience, expected) &&
            IsBareResource(audience);
    }

    private static bool HasSameOrigin(Uri audience, Uri expected)
    {
        return string.Equals(audience.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(audience.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            audience.Port == expected.Port;
    }

    private static bool HasSamePath(Uri audience, Uri expected)
    {
        return string.Equals(audience.AbsolutePath.TrimEnd('/'), expected.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
    }

    private static bool IsBareResource(Uri audience)
    {
        return string.IsNullOrEmpty(audience.Query.TrimStart('?')) &&
            string.IsNullOrEmpty(audience.Fragment) &&
            string.IsNullOrEmpty(audience.UserInfo);
    }
}
