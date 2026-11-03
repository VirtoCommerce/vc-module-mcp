using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Services;

namespace VirtoCommerce.MCP.Web.Filters;

public static class McpScopeSessionConfigurator
{
    private static readonly string _version = typeof(McpScopeSessionConfigurator).Assembly.GetName().Version?.ToString(3);

    public static async Task ConfigureAsync(HttpContext context, McpServerOptions options)
    {
        var services = context.RequestServices;
        var scope = context.GetMcpScope();
        var tools = services.GetServices<McpServerTool>();

        if (scope != null)
        {
            var disabledToolNames = await services.GetRequiredService<McpToolAvailabilityService>().GetDisabledToolNamesAsync(scope.Name);
            tools = tools.Where(tool => !McpToolAvailabilityService.IsDisabled(disabledToolNames, tool.ProtocolTool.Name));
        }

        // Prompts and resources have no call filter of their own, so the ones whose [McpPermission] the user does not have
        // are left out of the request's collections: not listed, and not readable.
        var authorizationService = services.GetRequiredService<McpToolAuthorizationService>();

        ApplyScope(
            options,
            scope,
            tools,
            services.GetServices<McpServerPrompt>().Where(x => authorizationService.HasMcpPermissions(context.User, x)),
            services.GetServices<McpServerResource>().Where(x => authorizationService.HasMcpPermissions(context.User, x)));
    }

    public static void ApplyScope(
        McpServerOptions options,
        McpScope scope,
        IEnumerable<McpServerTool> tools,
        IEnumerable<McpServerPrompt> prompts,
        IEnumerable<McpServerResource> resources)
    {
        RefillInPlace(options.ToolCollection, tools, scope);
        RefillInPlace(options.PromptCollection, prompts, scope);
        RefillInPlace(options.ResourceCollection, resources, scope);

        if (scope != null)
        {
            options.ServerInfo = new Implementation
            {
                Name = scope.Title ?? scope.Name,
                Version = scope.Version ?? _version,
            };
            options.ServerInstructions = scope.Instructions;
        }
    }

    public static IList<(string ScopeName, string Name)> FindDuplicateNames(IEnumerable<IMcpServerPrimitive> primitives)
    {
        return primitives
            .SelectMany(primitive =>
            {
                var scopes = primitive.GetMcpScopes();
                return scopes.Count == 0
                    ? [(ScopeName: null, Name: primitive.Id)]
                    : scopes.Select(scopeName => (ScopeName: scopeName.ToLowerInvariant(), Name: primitive.Id));
            })
            .GroupBy(x => x)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();
    }

    private static void RefillInPlace<T>(McpServerPrimitiveCollection<T> collection, IEnumerable<T> registered, McpScope scope)
        where T : IMcpServerPrimitive
    {
        if (collection == null)
        {
            return;
        }

        collection.Clear();

        foreach (var primitive in registered.Where(x => IsVisible(x, scope)))
        {
            collection.TryAdd(primitive);
        }
    }

    public static bool IsVisible(IMcpServerPrimitive primitive, McpScope scope)
    {
        if (primitive.Metadata.OfType<McpApprovalToolAttribute>().Any() && scope?.SupportsApproval != true)
        {
            return false;
        }

        if (scope == null)
        {
            return primitive.GetMcpScopes().Count == 0;
        }

        return primitive.IsInMcpScope(scope.Name) &&
            (!scope.RequireAuthenticatedUser || primitive is not McpServerTool || DeclaresAccess(primitive));
    }

    // Who may use the tool: users with a permission ([Authorize], [McpPermission]), every signed-in user ([McpAnyUser])
    // or anonymous users too ([AllowAnonymous]). A scope that requires a signed-in user serves only tools that say so.
    public static bool DeclaresAccess(IMcpServerPrimitive primitive)
    {
        return primitive.Metadata.Any(x => x is IAuthorizeData or IAllowAnonymous or McpPermissionAttribute or McpAnyUserAttribute);
    }

    // The tools of scopes that require a signed-in user that declare no access, and are therefore not served there.
    public static IList<(string ScopeName, string Name)> FindToolsWithoutDeclaredAccess(IEnumerable<McpServerTool> tools, IEnumerable<McpScope> scopes)
    {
        var toolList = tools.ToList();

        return scopes
            .Where(scope => scope.RequireAuthenticatedUser)
            .SelectMany(scope => toolList
                .Where(tool => !DeclaresAccess(tool) && tool.IsInMcpScope(scope.Name) &&
                    (scope.SupportsApproval || !tool.Metadata.OfType<McpApprovalToolAttribute>().Any()))
                .Select(tool => (ScopeName: scope.Name, Name: tool.ProtocolTool.Name)))
            .ToList();
    }
}
