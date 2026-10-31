using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;
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
        McpChallengeWriter challengeWriter,
        IAuthorizationPolicyProvider policyProvider,
        IAuthorizationService authorizationService)
    {
        var scope = context.GetMcpScope();
        var challenge = new McpChallenge
        {
            Scope = scope,
            Origin = await originResolver.GetOriginAsync(context, scope),
            EndpointPath = context.GetMcpEndpointPath(),
        };

        string tokenError = null;
        if (HasBearerHeader(context.Request))
        {
            tokenError = await GetTokenErrorAsync(context.User, challenge.Resource, tokenValidator, context.RequestAborted);
        }
        else
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        if (tokenError != null)
        {
            challenge.Error = "invalid_token";
            challenge.ErrorDescription = tokenError;
            await challengeWriter.WriteChallengeAsync(context, challenge);
            return;
        }

        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        if (!isAuthenticated && scope.RequireAuthenticatedUser)
        {
            challenge.ErrorDescription = "Sign in to use this MCP server.";
            await challengeWriter.WriteChallengeAsync(context, challenge);
            return;
        }

        if (!isAuthenticated)
        {
            var toolName = await FindToolThatRequiresUserAsync(context, scope);
            if (toolName != null)
            {
                challenge.ToolName = toolName;
                challenge.ErrorDescription = $"Sign in to use tool '{toolName}'.";
                await challengeWriter.WriteChallengeAsync(context, challenge);
                return;
            }
        }

        var missingPermission = isAuthenticated
            ? await FindMissingPermissionAsync(context.User, scope, policyProvider, authorizationService)
            : null;
        if (missingPermission != null)
        {
            await challengeWriter.WriteForbiddenAsync(context, scope, $"The '{missingPermission}' permission is required to use this MCP server.");
            return;
        }

        await next(context);
    }

    // In a scope open to anonymous users, a call to a tool that needs a user is answered with a challenge instead of
    // an error, so the MCP client signs the user in and repeats the call.
    private static async Task<string> FindToolThatRequiresUserAsync(HttpContext context, McpScope scope)
    {
        var (method, toolName) = await McpJsonRpcRequestReader.ReadAsync(context.Request);
        if (method != RequestMethods.ToolsCall || string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        var tool = context.RequestServices.GetServices<McpServerTool>()
            .FirstOrDefault(x => x.IsInMcpScope(scope.Name) && string.Equals(x.ProtocolTool.Name, toolName, StringComparison.Ordinal));

        return tool != null && RequiresUser(tool) ? toolName : null;
    }

    public static bool RequiresUser(IMcpServerPrimitive primitive)
    {
        if (primitive.Metadata.OfType<McpRequiresUserAttribute>().Any() || primitive.Metadata.OfType<McpPermissionAttribute>().Any())
        {
            return true;
        }

        return !primitive.Metadata.OfType<IAllowAnonymous>().Any() && primitive.Metadata.OfType<IAuthorizeData>().Any();
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
}
