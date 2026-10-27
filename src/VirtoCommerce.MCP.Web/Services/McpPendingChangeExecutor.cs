using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Services;

public class McpPendingChangeExecutor(
    McpAuditWriter auditWriter,
    ILogger<McpPendingChangeExecutor> logger)
{
    public virtual async Task<CallToolResult> ExecuteAsync(
        RequestContext<CallToolRequestParams> requestContext,
        McpServerTool tool,
        McpPendingChange change)
    {
        var audit = auditWriter.Create(change.ScopeName, change.ToolName, requestContext.User);
        audit.PendingChangeId = change.Id;
        var stopwatch = Stopwatch.StartNew();

        Dictionary<string, JsonElement> arguments = null;
        CallToolResult result;
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(change.Arguments ?? "{}", McpJsonUtilities.DefaultOptions);
            var parameters = new CallToolRequestParams { Name = change.ToolName, Arguments = arguments };
            var jsonRpcRequest = new JsonRpcRequest
            {
                Id = requestContext.JsonRpcRequest.Id,
                Method = RequestMethods.ToolsCall,
                Params = JsonSerializer.SerializeToNode(parameters, McpJsonUtilities.DefaultOptions),
            };

            var toolContext = new RequestContext<CallToolRequestParams>(requestContext.Server, jsonRpcRequest, parameters)
            {
                Services = requestContext.Services,
                User = requestContext.User,
                MatchedPrimitive = tool,
            };

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
