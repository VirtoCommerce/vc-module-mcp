using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core;

namespace VirtoCommerce.MCP.Testing;

// Calls the tools of a module in-process, as a given user, the way the MCP module serves them in a scope: the tools are
// registered with the module's own AddMcpCapabilities, a user sees only the tools whose permissions they have, a call
// that needs approval runs as a dry run and ends as PendingApproval, and McpForbiddenException ends as Forbidden.
// It needs no platform, database or HTTP server: the test registers the services the tools use.
//
//   await using var harness = McpToolHarness.Create<McpAssemblyMarker>(services => services.AddSingleton(promotionStore));
//   var visible = harness.GetVisibleToolNames(McpTestUser.WithPermissions("marketer", "marketing:read"));
//   var result = await harness.CallAsync(marketer, "pause_promotion", new { promotion_id = "p1", dryRun = true });
//
// It reproduces the rules the module applies; it does not run the module's call filters, audit or approval storage.
public sealed class McpToolHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions _argumentOptions = new(JsonSerializerDefaults.General);

    private readonly ServiceProvider _services;
    private readonly StreamServerTransport _transport;
    private readonly McpServer _server;
    private readonly Dictionary<string, McpServerTool> _tools;

    private McpToolHarness(ServiceProvider services, McpScope scope)
    {
        _services = services;
        Scope = scope;

        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        _transport = new StreamServerTransport(Stream.Null, Stream.Null, loggerFactory: loggerFactory);
        _server = McpServer.Create(_transport, new McpServerOptions(), loggerFactory, services);

        _tools = services.GetServices<McpServerTool>()
            .Where(x => x.IsInMcpScope(scope.Name))
            .GroupBy(x => x.ProtocolTool.Name, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    }

    // The scope the tools are served in: by default the admin scope of the MCP module, which requires a signed-in user
    // and has approval (mcp:approve).
    public McpScope Scope { get; }

    // The tools of the scope, by name.
    public IReadOnlyCollection<McpServerTool> Tools => _tools.Values;

    public IServiceProvider Services => _services;

    // Tools switched off, as with MCP.Tools.Disabled: their names, or "*" for the whole scope.
    public ISet<string> DisabledTools { get; } = new HashSet<string>(StringComparer.Ordinal);

    public static McpToolHarness Create<TAssemblyMarker>(Action<IServiceCollection> configureServices = null, McpScope scope = null)
    {
        return Create(typeof(TAssemblyMarker).Assembly, configureServices, scope);
    }

    public static McpToolHarness Create(Assembly assembly, Action<IServiceCollection> configureServices = null, McpScope scope = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Create(services => services.AddMcpCapabilities(assembly), configureServices, scope);
    }

    // Tools registered by the test itself, such as services.AddMcpTools<MyTools>().
    public static McpToolHarness Create(Action<IServiceCollection> registerTools, Action<IServiceCollection> configureServices, McpScope scope)
    {
        ArgumentNullException.ThrowIfNull(registerTools);

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        configureServices?.Invoke(services);
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        registerTools(services);

        return new McpToolHarness(services.BuildServiceProvider(), scope ?? DefaultScope());
    }

    public static McpScope DefaultScope()
    {
        return new McpScope
        {
            Name = McpScopes.Admin,
            Title = "Test",
            RequireAuthenticatedUser = true,
            ApprovalPermission = ModuleConstants.Security.Permissions.Approve,
        };
    }

    public McpServerTool GetTool(string name)
    {
        return name != null && _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    // The names of the tools the user sees in tools/list, sorted.
    public IReadOnlyList<string> GetVisibleToolNames(ClaimsPrincipal user)
    {
        return _tools.Values
            .Where(x => IsVisible(x, user))
            .Select(x => x.ProtocolTool.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // Whether the user sees the tool: it is switched on, says who may use it, and the user has its permissions.
    public bool IsVisible(McpServerTool tool, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (DisabledTools.Contains("*") || DisabledTools.Contains(tool.ProtocolTool.Name))
        {
            return false;
        }

        var metadata = tool.Metadata;
        var authenticated = user?.Identity?.IsAuthenticated == true;

        if (Scope.RequireAuthenticatedUser)
        {
            var saysWhoMayUse = metadata.Any(x => x is IAuthorizeData or IAllowAnonymous or McpPermissionAttribute or McpAnyUserAttribute);
            if (!saysWhoMayUse || !authenticated)
            {
                return false;
            }
        }

        var mcpPermissions = metadata.OfType<McpPermissionAttribute>().SelectMany(x => x.Permissions).ToList();
        if (mcpPermissions.Count > 0 && (!authenticated || mcpPermissions.Any(x => user.GetMcpPermissionGrant(x) == null)))
        {
            return false;
        }

        if (metadata.OfType<McpApproverOnlyAttribute>().Any() && !IsApprover(user))
        {
            return false;
        }

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return true;
        }

        return metadata.OfType<IAuthorizeData>().All(x => IsAuthorized(user, x));
    }

    // Calls the tool as the user, with the arguments given as an object or a dictionary, with the names the tool
    // declares: new { promotion_id = "p1", dryRun = true }.
    public async Task<McpToolCallResult> CallAsync(ClaimsPrincipal user, string toolName, object arguments = null, McpToolCallOptions options = null, CancellationToken cancellationToken = default)
    {
        options ??= new McpToolCallOptions();

        var tool = GetTool(toolName);
        if (tool == null)
        {
            return new McpToolCallResult { ToolName = toolName, Outcome = McpToolCallOutcome.NotFound, Error = $"No tool '{toolName}' in scope '{Scope.Name}'." };
        }

        if (!IsVisible(tool, user))
        {
            return new McpToolCallResult { ToolName = toolName, Outcome = McpToolCallOutcome.NotAvailable, Error = $"Tool '{toolName}' is not available to this user." };
        }

        var values = ToArguments(arguments);
        var dryRunParameter = tool.FindDryRunParameter();
        var isDryRun = dryRunParameter != null && values.TryGetValue(dryRunParameter, out var dryRun) && dryRun.ValueKind == JsonValueKind.True;
        var askedForConfirmation = !isDryRun && tool.GetConfirmation() != null;

        if (askedForConfirmation && options.DeclineConfirmation)
        {
            return new McpToolCallResult { ToolName = toolName, Outcome = McpToolCallOutcome.Declined, AskedForConfirmation = true, Error = "The user did not confirm the call, so nothing was done." };
        }

        var needsApproval = !isDryRun && !options.AsApprovedChange && tool.RequiresApproval(values);
        if (needsApproval)
        {
            if (dryRunParameter == null)
            {
                return new McpToolCallResult { ToolName = toolName, Outcome = McpToolCallOutcome.Failed, Error = $"Tool '{toolName}' requires approval but has no dryRun parameter; the MCP module refuses its calls." };
            }

            if (!Scope.SupportsApproval)
            {
                return new McpToolCallResult { ToolName = toolName, Outcome = McpToolCallOutcome.Failed, Error = $"Tool '{toolName}' requires approval, which scope '{Scope.Name}' does not have." };
            }

            values[dryRunParameter] = JsonSerializer.SerializeToElement(true);
        }

        var result = await InvokeAsync(tool, user, values, cancellationToken);

        return new McpToolCallResult
        {
            ToolName = toolName,
            Outcome = result.Outcome == McpToolCallOutcome.Succeeded && needsApproval ? McpToolCallOutcome.PendingApproval : result.Outcome,
            Result = result.Result,
            Error = result.Error,
            AskedForConfirmation = askedForConfirmation,
            WasDryRun = isDryRun || needsApproval,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _transport.DisposeAsync();
        await _services.DisposeAsync();
    }

    private async Task<McpToolCallResult> InvokeAsync(McpServerTool tool, ClaimsPrincipal user, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();

        var httpContext = new DefaultHttpContext { User = user ?? McpTestUser.Anonymous(), RequestServices = scope.ServiceProvider };
        httpContext.SetMcpScope(Scope);
        var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
        if (accessor != null)
        {
            accessor.HttpContext = httpContext;
        }

        var parameters = new CallToolRequestParams { Name = tool.ProtocolTool.Name, Arguments = arguments };
        var request = new JsonRpcRequest
        {
            Id = new RequestId(Guid.NewGuid().ToString("N")),
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions),
        };
        var context = new RequestContext<CallToolRequestParams>(_server, request, parameters)
        {
            Services = scope.ServiceProvider,
            User = httpContext.User,
            MatchedPrimitive = tool,
        };

        try
        {
            var result = await tool.InvokeAsync(context, cancellationToken);
            if (result.IsError == true)
            {
                var message = string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(x => x.Text));
                return new McpToolCallResult { Outcome = McpToolCallOutcome.Failed, Result = result, Error = message };
            }

            return new McpToolCallResult { Outcome = McpToolCallOutcome.Succeeded, Result = result };
        }
        catch (McpForbiddenException ex)
        {
            return new McpToolCallResult { Outcome = McpToolCallOutcome.Forbidden, Error = ex.Message };
        }
        catch (McpException ex)
        {
            return new McpToolCallResult { Outcome = McpToolCallOutcome.Failed, Error = ex.Message };
        }
        finally
        {
            if (accessor != null)
            {
                accessor.HttpContext = null;
            }
        }
    }

    private bool IsApprover(ClaimsPrincipal user)
    {
        return Scope.SupportsApproval && user?.Identity?.IsAuthenticated == true && HasGlobalPermission(user, Scope.ApprovalPermission);
    }

    // The platform's permission policies: [Authorize("x")] needs the permission granted globally (a scoped grant does not
    // count), or the administrator role; roles are checked as roles.
    private static bool IsAuthorized(ClaimsPrincipal user, IAuthorizeData authorizeData)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(authorizeData.Roles) &&
            !authorizeData.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(user.IsInRole))
        {
            return false;
        }

        return string.IsNullOrEmpty(authorizeData.Policy) || HasGlobalPermission(user, authorizeData.Policy);
    }

    private static bool HasGlobalPermission(ClaimsPrincipal user, string permission)
    {
        return user.IsInRole(PlatformConstants.Security.SystemRoles.Administrator) ||
               user.FindAll(PlatformConstants.Security.Claims.PermissionClaimType).Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, JsonElement> ToArguments(object arguments)
    {
        if (arguments == null)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        if (arguments is IDictionary<string, JsonElement> elements)
        {
            return new Dictionary<string, JsonElement>(elements, StringComparer.Ordinal);
        }

        var json = arguments as JsonElement? ?? JsonSerializer.SerializeToElement(arguments, arguments.GetType(), _argumentOptions);
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Arguments must be an object or a dictionary.", nameof(arguments));
        }

        return json.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
    }
}

public sealed class McpToolCallOptions
{
    // Run the call as an approved change: the tool runs for real even when it requires approval.
    public bool AsApprovedChange { get; init; }

    // Answer the confirmation question of [McpRequiresConfirmation] with Decline; by default the harness confirms.
    public bool DeclineConfirmation { get; init; }
}
