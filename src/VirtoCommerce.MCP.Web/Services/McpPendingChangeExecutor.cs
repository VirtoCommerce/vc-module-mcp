using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;

namespace VirtoCommerce.MCP.Web.Services;

public class McpPendingChangeExecutor(
    McpAuditWriter auditWriter,
    ILoggerFactory loggerFactory,
    ILogger<McpPendingChangeExecutor> logger)
{
    public virtual Task<CallToolResult> ExecuteAsync(
        RequestContext<CallToolRequestParams> requestContext,
        McpServerTool tool,
        McpPendingChange change)
    {
        return ExecuteAsync(requestContext.Server, requestContext.JsonRpcRequest.Id, requestContext.Services, requestContext.User, tool, change, clientId: null);
    }

    // Runs an approved change outside an MCP session, when it is approved in the back office. The tool gets a server that
    // is not connected to any client, so progress and log notifications it sends go nowhere, and finds the change's
    // scope on the request. The audit entry names the client that ran it, for example the back office.
    public virtual async Task<CallToolResult> ExecuteAsync(
        IServiceProvider services,
        ClaimsPrincipal user,
        McpScope scope,
        McpServerTool tool,
        McpPendingChange change,
        string clientId)
    {
        var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext != null && httpContext.GetMcpScope() == null)
        {
            httpContext.SetMcpScope(scope);
        }

        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null, loggerFactory: loggerFactory);
        await using var server = McpServer.Create(transport, new McpServerOptions(), loggerFactory, services);

        return await ExecuteAsync(server, new RequestId(change.Id), services, user, tool, change, clientId);
    }

    // Why the change cannot be applied for its requester, or null when it can. The change runs as the approver, whose
    // access may be wider, so the tool's dry run is repeated as the requester first: a requester who lost the tool's
    // permission, or a change outside the stores or sellers the requester may work with, is refused.
    public virtual Task<string> FindRequesterRefusalAsync(
        RequestContext<CallToolRequestParams> requestContext,
        McpServerTool tool,
        McpPendingChange change)
    {
        return FindRequesterRefusalAsync(requestContext.Server, requestContext.Services, tool, change);
    }

    // The same check for a change approved outside an MCP session, for example in the back office.
    public virtual async Task<string> FindRequesterRefusalAsync(IServiceProvider services, McpScope scope, McpServerTool tool, McpPendingChange change)
    {
        var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext != null && httpContext.GetMcpScope() == null)
        {
            httpContext.SetMcpScope(scope);
        }

        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null, loggerFactory: loggerFactory);
        await using var server = McpServer.Create(transport, new McpServerOptions(), loggerFactory, services);

        return await FindRequesterRefusalAsync(server, services, tool, change);
    }

    protected virtual async Task<string> FindRequesterRefusalAsync(McpServer server, IServiceProvider services, McpServerTool tool, McpPendingChange change)
    {
        var dryRunParameter = tool.FindDryRunParameter();
        if (dryRunParameter == null)
        {
            return $"tool '{change.ToolName}' has no dry run to check the change against its requester's access";
        }

        var requester = await services.GetRequiredService<McpUserPrincipalFactory>().CreateAsync(change.RequestedByUserId);
        if (requester == null)
        {
            return "the account of its requester no longer exists or is locked";
        }

        if (!await services.GetRequiredService<McpToolAuthorizationService>().IsAuthorizedAsync(requester, tool))
        {
            return $"its requester is no longer allowed to run tool '{change.ToolName}'";
        }

        // A switch that turns the tool off for some users, stores or organizations applies to the requester too.
        var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;
        var availabilityContext = new McpToolAvailabilityContext(change.ScopeName, change.ToolName, requester, httpContext);
        if (await services.GetRequiredService<IMcpToolAvailabilityService>().IsDisabledAsync(availabilityContext))
        {
            return $"tool '{change.ToolName}' is switched off for its requester";
        }

        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.Arguments ?? "{}", McpJsonUtilities.DefaultOptions);
        arguments[dryRunParameter] = JsonSerializer.SerializeToElement(true);

        try
        {
            var result = await InvokeToolAsync(tool, CreateToolContext(server, new RequestId(change.Id), services, requester, tool, change.ToolName, arguments), change.ScopeName);

            return result.IsError == true ? $"its dry run as the requester failed: {GetText(result)}" : null;
        }
        catch (McpException ex)
        {
            return $"its dry run as the requester failed: {ex.Message}";
        }
    }

    // The audit entry of a review decision made outside an agent's tool call, for example in the back office.
    public virtual Task WriteReviewAuditAsync(ClaimsPrincipal user, McpPendingChange change, string toolName, string clientId)
    {
        var audit = auditWriter.Create(change.ScopeName, toolName, user);
        audit.ClientId = clientId;
        audit.PendingChangeId = change.Id;
        audit.Outcome = McpAuditOutcome.Succeeded;
        audit.TraceId = Activity.Current?.TraceId.ToString();

        return auditWriter.WriteAsync(audit, arguments: null, sensitiveArgumentPaths: []);
    }

    protected virtual async Task<CallToolResult> ExecuteAsync(
        McpServer server,
        RequestId requestId,
        IServiceProvider services,
        ClaimsPrincipal user,
        McpServerTool tool,
        McpPendingChange change,
        string clientId)
    {
        var audit = auditWriter.Create(change.ScopeName, change.ToolName, user);
        audit.ClientId ??= clientId;
        audit.PendingChangeId = change.Id;
        audit.TraceId = Activity.Current?.TraceId.ToString();
        var stopwatch = Stopwatch.StartNew();

        Dictionary<string, JsonElement> arguments = null;
        CallToolResult result;
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.Arguments ?? "{}", McpJsonUtilities.DefaultOptions);
            var toolContext = CreateToolContext(server, requestId, services, user, tool, change.ToolName, arguments);
            toolContext.SetExecutedPendingChange(change.Id, change.RequestedByUserId);

            result = await InvokeToolAsync(tool, toolContext, change.ScopeName);
            audit.Outcome = result.IsError == true ? McpAuditOutcome.Failed : McpAuditOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Approved MCP change {ChangeId} failed in tool {ToolName}", change.Id, change.ToolName);
            audit.Outcome = McpAuditWriter.GetFailureOutcome(ex);
            audit.ErrorMessage = ex.Message;
            result = new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = ex is McpException ? ex.Message : $"An unexpected error occurred while executing tool '{change.ToolName}'." }],
            };
        }

        audit.DurationMs = stopwatch.ElapsedMilliseconds;
        await auditWriter.WriteAsync(audit, arguments, change.SensitiveArgumentPaths);

        return result;
    }

    // The tool inside the scope's call filters (AddMcpCallToolFilter), in the order they run around an agent's call, so
    // an approved change meets the same per-tool policy when it runs as the call that submitted it did.
    protected virtual async Task<CallToolResult> InvokeToolAsync(McpServerTool tool, RequestContext<CallToolRequestParams> context, string scopeName)
    {
        McpRequestHandler<CallToolRequestParams, CallToolResult> handler = (toolContext, cancellationToken) => tool.InvokeAsync(toolContext, cancellationToken);

        var registrations = context.Services.GetServices<McpCallToolFilterRegistration>().Where(x => x.AppliesTo(scopeName)).Reverse();
        foreach (var registration in registrations)
        {
            var next = handler;
            var filter = (IMcpCallToolFilter)context.Services.GetRequiredService(registration.FilterType);
            handler = (toolContext, cancellationToken) => filter.InvokeAsync(next, toolContext, cancellationToken);
        }

        return await handler(context, CancellationToken.None);
    }

    private static RequestContext<CallToolRequestParams> CreateToolContext(
        McpServer server,
        RequestId requestId,
        IServiceProvider services,
        ClaimsPrincipal user,
        McpServerTool tool,
        string toolName,
        Dictionary<string, JsonElement> arguments)
    {
        var parameters = new CallToolRequestParams { Name = toolName, Arguments = arguments };
        var jsonRpcRequest = new JsonRpcRequest
        {
            Id = requestId,
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions),
        };

        return new RequestContext<CallToolRequestParams>(server, jsonRpcRequest, parameters)
        {
            Services = services,
            User = user,
            MatchedPrimitive = tool,
        };
    }

    private static string GetText(CallToolResult result)
    {
        return string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(x => x.Text));
    }
}
