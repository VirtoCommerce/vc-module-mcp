using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        ApplyScope(
            options,
            scope,
            tools,
            services.GetServices<McpServerPrompt>(),
            services.GetServices<McpServerResource>());
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

    private static bool IsVisible(IMcpServerPrimitive primitive, McpScope scope)
    {
        if (primitive.Metadata.OfType<McpApprovalToolAttribute>().Any() && scope?.SupportsApproval != true)
        {
            return false;
        }

        return scope != null
            ? primitive.IsInMcpScope(scope.Name)
            : primitive.GetMcpScopes().Count == 0;
    }
}
