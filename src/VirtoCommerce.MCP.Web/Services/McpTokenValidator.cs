using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using VirtoCommerce.Platform.Security.Model.OpenIddict;

namespace VirtoCommerce.MCP.Web.Services;

public class McpTokenValidator(
    IOpenIddictTokenManager tokenManager)
{
    private const string AudienceClaimType = "aud";

    public static bool HasExpectedAudience(ClaimsPrincipal principal, string expectedResource)
    {
        if (!Uri.TryCreate(expectedResource, UriKind.Absolute, out var expected))
        {
            return false;
        }

        return GetAudiences(principal).Any(value => IsExpectedAudience(value, expected));
    }

    public virtual async Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var tokenId = principal.GetTokenId();
        if (string.IsNullOrEmpty(tokenId))
        {
            // The platform issues tokens without an id when its token storage is disabled. Such a token is not in the store,
            // so it cannot be revoked there and stays valid until it expires; refusing it would lock MCP out of such deployments.
            return true;
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
