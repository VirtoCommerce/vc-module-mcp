using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Services;

namespace VirtoCommerce.MCP.Web.Middleware;

public class McpScopeAuthenticationMiddleware(
    RequestDelegate next,
    ILogger<McpScopeAuthenticationMiddleware> logger)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(
        HttpContext context,
        IMcpTokenValidator tokenValidator,
        IAuthorizationPolicyProvider policyProvider,
        IAuthorizationService authorizationService,
        IHostEnvironment environment)
    {
        var scope = context.GetMcpScope();
        var originResolver = context.RequestServices.GetMcpScopeService<IMcpPublicOriginResolver>(scope);
        var challengeWriter = context.RequestServices.GetMcpScopeService<McpChallengeWriter>(scope);
        var challenge = new McpChallenge
        {
            Scope = scope,
            Origin = await originResolver.GetOriginAsync(context, scope),
            EndpointPath = context.GetMcpEndpointPath(),
        };

        var tokenError = await AuthenticateAsync(context, challenge, originResolver, tokenValidator, environment);
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

    // Sets the request's user from its bearer token, or an anonymous user without one. Returns why the token is refused.
    private static async Task<string> AuthenticateAsync(
        HttpContext context,
        McpChallenge challenge,
        IMcpPublicOriginResolver originResolver,
        IMcpTokenValidator tokenValidator,
        IHostEnvironment environment)
    {
        if (!HasBearerHeader(context.Request))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            return null;
        }

        context.User = await tokenValidator.AuthenticateAsync(context) ?? new ClaimsPrincipal(new ClaimsIdentity());

        // The audience a token must have is this server's address. Taken from the request's Host and forwarded
        // headers, it would be whatever host the client was allowed to get a token for.
        if (!originResolver.IsOriginConfigured(context, challenge.Scope) && !environment.IsDevelopment())
        {
            return "This MCP server has no public origin configured (MCP:PublicOrigin), so it cannot check that a token was issued for it. Ask the administrator to set it.";
        }

        return await tokenValidator.GetTokenErrorAsync(context.User, challenge.Resource, context.RequestAborted);
    }

    // In a scope open to anonymous users, a call to a tool that needs a user is answered with a challenge instead of
    // an error, so the MCP client signs the user in and repeats the call. A request that names several tools is
    // challenged when any of them needs a user.
    private static async Task<string> FindToolThatRequiresUserAsync(HttpContext context, McpScope scope)
    {
        var request = await McpJsonRpcRequestReader.GetOrReadAsync(context);
        if (request.Method != RequestMethods.ToolsCall)
        {
            return null;
        }

        // The tools the session of the scope lists: a tool of the same name in another scope does not count.
        var tools = context.RequestServices.GetRequiredService<McpToolCatalog>().GetTools(scope)
            .Where(x => McpScopeSessionConfigurator.IsVisible(x, scope))
            .ToList();

        return request.ToolNames.FirstOrDefault(toolName => tools.Any(x =>
            string.Equals(x.ProtocolTool.Name, toolName, StringComparison.Ordinal) && RequiresUser(x)));
    }

    public static bool RequiresUser(IMcpServerPrimitive primitive)
    {
        if (primitive.Metadata.Any(x => x is McpRequiresUserAttribute or McpPermissionAttribute or McpAnyUserAttribute))
        {
            return true;
        }

        return !primitive.Metadata.OfType<IAllowAnonymous>().Any() && primitive.Metadata.OfType<IAuthorizeData>().Any();
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
