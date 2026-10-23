using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Filters;

public class McpCallToolFilter(
    IHttpContextAccessor httpContextAccessor,
    IMcpApprovalService approvalService,
    McpToolAuthorizationService toolAuthorizationService,
    McpAuditWriter auditWriter,
    ILogger<McpCallToolFilter> logger)
{
    private const string ForbiddenMessage = "Access forbidden: This tool requires authorization.";

    private static readonly string[] _dryRunParameterNames = ["dryRun", "dry_run"];

    public virtual async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var scope = httpContextAccessor.HttpContext.GetMcpScope();
        if (scope == null)
        {
            return await next(context, cancellationToken);
        }

        var toolName = context.Params?.Name;
        var arguments = context.Params?.Arguments;
        var audit = auditWriter.Create(scope.Name, toolName, context.User);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var tool = context.MatchedPrimitive as McpServerTool;
            if (tool != null && !await toolAuthorizationService.IsAuthorizedAsync(context.User, tool))
            {
                audit.Outcome = McpAuditOutcome.Forbidden;
                throw new McpProtocolException(ForbiddenMessage, McpErrorCode.InvalidRequest);
            }

            ValidateRequiredArguments(context);

            CallToolResult result;
            if (tool != null && tool.RequiresApproval() && !IsDryRunCall(tool, arguments))
            {
                if (!CanBeApprovedIn(scope.Name))
                {
                    audit.Outcome = McpAuditOutcome.Error;
                    throw new McpException($"Tool '{toolName}' requires approval, which is available in the '{McpScopes.Admin}' scope only. The call was not executed.");
                }

                var change = await SubmitForApprovalAsync(next, context, scope, tool, cancellationToken);
                audit.Outcome = McpAuditOutcome.PendingApproval;
                audit.PendingChangeId = change.Id;
                result = CreatePendingApprovalResult(change);
            }
            else
            {
                result = await next(context, cancellationToken);
                audit.Outcome = result.IsError == true ? McpAuditOutcome.Failed : McpAuditOutcome.Succeeded;
                audit.ErrorMessage = result.IsError == true ? GetText(result) : null;
            }

            return result;
        }
        catch (McpProtocolException ex)
        {
            audit.Outcome ??= McpAuditOutcome.Error;
            audit.ErrorMessage = ex.Message;
            throw;
        }
        catch (McpException ex)
        {
            audit.Outcome ??= McpAuditOutcome.Failed;
            audit.ErrorMessage = ex.Message;
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MCP tool {ToolName} failed in scope {ScopeName}", toolName, scope.Name);
            audit.Outcome = McpAuditOutcome.Error;
            audit.ErrorMessage = ex.Message;

            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"An unexpected error occurred while executing tool '{toolName}'." }],
            };
        }
        finally
        {
            stopwatch.Stop();
            audit.DurationMs = stopwatch.ElapsedMilliseconds;
            audit.PendingChangeId ??= context.GetAuditPendingChangeId();

            if (audit.Outcome != null)
            {
                logger.LogInformation("MCP tool {ToolName} called in scope {ScopeName} by {UserName}: {Outcome} in {DurationMs} ms",
                    toolName, scope.Name, audit.UserName, audit.Outcome, audit.DurationMs);
                await auditWriter.WriteAsync(audit, arguments);
            }
        }
    }

    public static bool CanBeApprovedIn(string scopeName)
    {
        return string.Equals(scopeName, McpScopes.Admin, StringComparison.OrdinalIgnoreCase);
    }

    public static IList<(string ScopeName, string Name)> FindToolsThatCannotBeApproved(IEnumerable<McpServerTool> tools)
    {
        return tools
            .Where(tool => tool.RequiresApproval())
            .SelectMany(tool =>
            {
                var scopes = tool.GetMcpScopes();
                return scopes.Count == 0
                    ? [(ScopeName: null, tool.ProtocolTool.Name)]
                    : scopes.Where(scopeName => !CanBeApprovedIn(scopeName)).Select(scopeName => (ScopeName: scopeName, tool.ProtocolTool.Name));
            })
            .Distinct()
            .ToList();
    }

    protected virtual async Task<McpPendingChange> SubmitForApprovalAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        McpScope scope,
        McpServerTool tool,
        CancellationToken cancellationToken)
    {
        var change = AbstractTypeFactory<McpPendingChange>.TryCreateInstance();
        change.ScopeName = scope.Name;
        change.ToolName = tool.ProtocolTool.Name;
        change.Arguments = JsonSerializer.Serialize(context.Params.Arguments ?? new Dictionary<string, JsonElement>(), McpJsonUtilities.DefaultOptions);
        change.RequestedByUserId = context.User?.GetUserId();
        change.RequestedByUserName = context.User?.GetUserName();

        var dryRunParameter = FindDryRunParameter(tool);
        if (dryRunParameter != null)
        {
            var preview = await InvokeDryRunAsync(next, context, dryRunParameter, cancellationToken);
            if (preview.IsError == true)
            {
                throw new McpException($"The change was not submitted for approval, because its dry run failed: {GetText(preview)}");
            }

            change.Preview = JsonSerializer.Serialize(preview, McpJsonUtilities.DefaultOptions);
        }

        return await approvalService.SubmitAsync(change);
    }

    protected virtual async Task<CallToolResult> InvokeDryRunAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        string dryRunParameter,
        CancellationToken cancellationToken)
    {
        var originalParams = context.Params;
        var arguments = new Dictionary<string, JsonElement>(originalParams.Arguments ?? new Dictionary<string, JsonElement>())
        {
            [dryRunParameter] = JsonSerializer.SerializeToElement(true),
        };

        context.Params = new CallToolRequestParams { Name = originalParams.Name, Arguments = arguments };
        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            context.Params = originalParams;
        }
    }

    protected static CallToolResult CreatePendingApprovalResult(McpPendingChange change)
    {
        var payload = new
        {
            status = "pending_approval",
            change_id = change.Id,
            expires_at = change.ExpirationDate,
            message = "The change was not applied. It is waiting for approval by a user with the mcp:approve permission. " +
                "Tell the user the change id; an approver applies it with approve_pending_change. " +
                "The requester can check it with get_pending_change and withdraw it with reject_pending_change.",
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, McpJsonUtilities.DefaultOptions) }],
        };
    }

    protected static void ValidateRequiredArguments(RequestContext<CallToolRequestParams> context)
    {
        if (context.MatchedPrimitive is not McpServerTool tool ||
            tool.ProtocolTool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var name in required.EnumerateArray().Select(x => x.GetString()))
        {
            if (context.Params?.Arguments == null ||
                !context.Params.Arguments.TryGetValue(name, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new McpProtocolException($"Argument '{name}' is required.", McpErrorCode.InvalidParams);
            }
        }
    }

    protected static bool IsDryRunCall(McpServerTool tool, IDictionary<string, JsonElement> arguments)
    {
        var dryRunParameter = FindDryRunParameter(tool);

        return dryRunParameter != null &&
            arguments?.TryGetValue(dryRunParameter, out var value) == true &&
            value.ValueKind == JsonValueKind.True;
    }

    protected static string FindDryRunParameter(McpServerTool tool)
    {
        var schema = tool.ProtocolTool.InputSchema;
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return _dryRunParameterNames.FirstOrDefault(name => properties.TryGetProperty(name, out _));
    }

    protected static string GetText(CallToolResult result)
    {
        return string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(x => x.Text));
    }
}
