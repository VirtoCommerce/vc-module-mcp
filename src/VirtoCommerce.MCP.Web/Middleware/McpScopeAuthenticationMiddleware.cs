using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;

namespace VirtoCommerce.MCP.Web.Middleware;

public class McpScopeAuthenticationMiddleware(
    RequestDelegate next,
    ILogger<McpScopeAuthenticationMiddleware> logger)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(
        HttpContext context,
        IMcpPublicOriginResolver originResolver,
        McpTokenValidator tokenValidator,
        IAuthorizationPolicyProvider policyProvider,
        IAuthorizationService authorizationService)
    {
        var scope = context.GetMcpScope();
        var origin = originResolver.GetOrigin(context, scope);

        string tokenError = null;
        if (HasBearerHeader(context.Request))
        {
            tokenError = await GetTokenErrorAsync(context.User, origin + scope.Path, tokenValidator, context.RequestAborted);
        }
        else
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        if (tokenError != null)
        {
            await WriteChallengeAsync(context, scope, origin, "invalid_token", tokenError);
            return;
        }

        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        if (!isAuthenticated && scope.RequireAuthenticatedUser)
        {
            await WriteChallengeAsync(context, scope, origin, error: null, "Sign in to use this MCP server.");
            return;
        }

        var missingPermission = isAuthenticated
            ? await FindMissingPermissionAsync(context.User, scope, policyProvider, authorizationService)
            : null;
        if (missingPermission != null)
        {
            await WriteForbiddenAsync(context, $"The '{missingPermission}' permission is required to use this MCP server.");
            return;
        }

        await next(context);
    }

    private static async Task<string> GetTokenErrorAsync(
        ClaimsPrincipal user,
        string resource,
        McpTokenValidator tokenValidator,
        CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return "The access token is invalid or expired.";
        }

        if (!McpTokenValidator.HasExpectedAudience(user, resource))
        {
            return "The access token was not issued for this MCP resource.";
        }

        return await tokenValidator.IsActiveAsync(user, cancellationToken)
            ? null
            : "The OAuth session has ended. Sign in again to continue.";
    }

    private async Task<string> FindMissingPermissionAsync(
        ClaimsPrincipal user,
        McpScope scope,
        IAuthorizationPolicyProvider policyProvider,
        IAuthorizationService authorizationService)
    {
        foreach (var permission in scope.Permissions)
        {
            var policy = await policyProvider.GetPolicyAsync(permission);
            if (policy == null)
            {
                logger.LogWarning("MCP scope {ScopeName} requires permission {Permission}, which is not registered. Is the module that defines it installed?",
                    scope.Name, permission);
                return permission;
            }

            var result = await authorizationService.AuthorizeAsync(user, resource: null, policy);
            if (!result.Succeeded)
            {
                LogMissingPermission(scope, user, permission);
                return permission;
            }
        }

        return null;
    }

    private void LogMissingPermission(McpScope scope, ClaimsPrincipal user, string permission)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("MCP scope {ScopeName} refused user {UserName}: permission {Permission} is missing",
                scope.Name, user.Identity?.Name, permission);
        }
    }

    private static bool HasBearerHeader(HttpRequest request)
    {
        var authorization = request.Headers.Authorization;

        return authorization.Count == 1 &&
            authorization[0].StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) &&
            authorization[0].Length > BearerPrefix.Length;
    }

    private static async Task WriteChallengeAsync(HttpContext context, McpScope scope, string origin, string error, string message)
    {
        var challenge = $"Bearer realm=\"{Escape(origin)}\", resource_metadata=\"{Escape(origin + scope.ProtectedResourceMetadataPath)}\", scope=\"{string.Join(' ', ModuleConstants.Security.OAuthScopes)}\"";
        if (!string.IsNullOrEmpty(error))
        {
            challenge += $", error=\"{Escape(error)}\", error_description=\"{Escape(message)}\"";
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = challenge;
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        await context.Response.WriteAsJsonAsync(new { error = error ?? "unauthorized", error_description = message }, context.RequestAborted);
    }

    private static async Task WriteForbiddenAsync(HttpContext context, string message)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        await context.Response.WriteAsJsonAsync(new { error = "forbidden", error_description = message }, context.RequestAborted);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
