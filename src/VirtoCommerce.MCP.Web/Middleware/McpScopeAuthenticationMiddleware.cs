using System;
using System.Security.Claims;
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

        if (!HasBearerHeader(context.Request))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
        else if (context.User?.Identity?.IsAuthenticated != true)
        {
            await WriteChallengeAsync(context, scope, origin, "invalid_token", "The access token is invalid or expired.");
            return;
        }
        else if (!McpTokenValidator.HasExpectedAudience(context.User, origin + scope.Path))
        {
            await WriteChallengeAsync(context, scope, origin, "invalid_token", "The access token was not issued for this MCP resource.");
            return;
        }
        else if (!await tokenValidator.IsActiveAsync(context.User, context.RequestAborted))
        {
            await WriteChallengeAsync(context, scope, origin, "invalid_token", "The OAuth session has ended. Sign in again to continue.");
            return;
        }

        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;

        if (scope.RequireAuthenticatedUser && !isAuthenticated)
        {
            await WriteChallengeAsync(context, scope, origin, error: null, "Sign in to use this MCP server.");
            return;
        }

        if (isAuthenticated)
        {
            foreach (var permission in scope.Permissions)
            {
                var policy = await policyProvider.GetPolicyAsync(permission);
                if (policy == null)
                {
                    logger.LogWarning("MCP scope {ScopeName} requires permission {Permission}, which is not registered. Is the module that defines it installed?",
                        scope.Name, permission);
                    await WriteForbiddenAsync(context, $"The '{permission}' permission is required to use this MCP server.");
                    return;
                }

                var result = await authorizationService.AuthorizeAsync(context.User, resource: null, policy);
                if (!result.Succeeded)
                {
                    logger.LogInformation("MCP scope {ScopeName} refused user {UserName}: permission {Permission} is missing",
                        scope.Name, context.User.Identity?.Name, permission);
                    await WriteForbiddenAsync(context, $"The '{permission}' permission is required to use this MCP server.");
                    return;
                }
            }
        }

        await next(context);
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
