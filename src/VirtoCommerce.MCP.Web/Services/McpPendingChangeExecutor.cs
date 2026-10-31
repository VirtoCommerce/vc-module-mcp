using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            var parameters = new CallToolRequestParams { Name = change.ToolName, Arguments = arguments };
            var jsonRpcRequest = new JsonRpcRequest
            {
                Id = requestId,
                Method = RequestMethods.ToolsCall,
                Params = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions),
            };

            var toolContext = new RequestContext<CallToolRequestParams>(server, jsonRpcRequest, parameters)
            {
                Services = services,
                User = user,
                MatchedPrimitive = tool,
            };
            toolContext.SetExecutedPendingChange(change.Id, change.RequestedByUserId);

            result = await tool.InvokeAsync(toolContext, CancellationToken.None);
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
}
