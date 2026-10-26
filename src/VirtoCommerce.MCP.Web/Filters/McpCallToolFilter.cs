using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    IMcpApprovalService approvalService,
    McpToolAuthorizationService toolAuthorizationService,
    McpAuditWriter auditWriter,
    ILogger<McpCallToolFilter> logger)
{
    private const string AuditedKey = "VirtoCommerce.MCP.Audit.Written";

    private static readonly string[] _dryRunParameterNames = ["dryRun", "dry_run"];

    public virtual async ValueTask<CallToolResult> InvokeAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        McpScope scope,
        CancellationToken cancellationToken)
    {
        var audit = auditWriter.Create(scope.Name, context.Params?.Name, context.User);
        context.Items[AuditedKey] = true;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await ExecuteAuditedAsync(next, context, scope, audit, cancellationToken);
        }
        catch (McpException ex)
        {
            audit.Outcome ??= McpAuditWriter.GetFailureOutcome(ex);
            audit.ErrorMessage = ex.Message;
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MCP tool {ToolName} failed in scope {ScopeName}", audit.ToolName, scope.Name);
            audit.Outcome = McpAuditOutcome.Error;
            audit.ErrorMessage = ex.Message;

            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"An unexpected error occurred while executing tool '{audit.ToolName}'." }],
            };
        }
        finally
        {
            audit.DurationMs = stopwatch.ElapsedMilliseconds;
            audit.PendingChangeId ??= context.GetAuditPendingChangeId();
            await WriteAuditAsync(audit, context.Params?.Arguments);
        }
    }

    public virtual async Task AuditRefusalAsync(RequestContext<CallToolRequestParams> context, McpScope scope, McpProtocolException exception)
    {
        var audit = auditWriter.Create(scope.Name, context.Params?.Name, context.User);
        audit.Outcome = await IsForbiddenAsync(context) ? McpAuditOutcome.Forbidden : McpAuditOutcome.Error;
        audit.ErrorMessage = exception.Message;

        await WriteAuditAsync(audit, context.Params?.Arguments);
    }

    internal static bool IsAudited(MessageContext context)
    {
        return context.Items.ContainsKey(AuditedKey);
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

    private async Task<CallToolResult> ExecuteAuditedAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        McpScope scope,
        McpAuditEntry audit,
        CancellationToken cancellationToken)
    {
        ValidateRequiredArguments(context);

        var tool = context.MatchedPrimitive as McpServerTool;

        if (tool != null && tool.RequiresApproval() && !IsDryRunCall(tool, context.Params?.Arguments))
        {
            return await HoldForApprovalAsync(next, context, scope, tool, audit, cancellationToken);
        }

        var result = await next(context, cancellationToken);
        audit.Outcome = result.IsError == true ? McpAuditOutcome.Failed : McpAuditOutcome.Succeeded;
        audit.ErrorMessage = result.IsError == true ? GetText(result) : null;

        return result;
    }

    private async Task<CallToolResult> HoldForApprovalAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> context,
        McpScope scope,
        McpServerTool tool,
        McpAuditEntry audit,
        CancellationToken cancellationToken)
    {
        if (!CanBeApprovedIn(scope.Name))
        {
            audit.Outcome = McpAuditOutcome.Error;
            throw new McpException($"Tool '{audit.ToolName}' requires approval, which is available in the '{McpScopes.Admin}' scope only. The call was not executed.");
        }

        var change = await SubmitForApprovalAsync(next, context, scope, tool, cancellationToken);
        audit.Outcome = McpAuditOutcome.PendingApproval;
        audit.PendingChangeId = change.Id;

        return CreatePendingApprovalResult(change);
    }

    private async Task<bool> IsForbiddenAsync(RequestContext<CallToolRequestParams> context)
    {
        return context.MatchedPrimitive is McpServerTool tool && !await toolAuthorizationService.IsAuthorizedAsync(context.User, tool);
    }

    private async Task WriteAuditAsync(McpAuditEntry audit, IDictionary<string, JsonElement> arguments)
    {
        if (audit.Outcome == null)
        {
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("MCP tool {ToolName} called in scope {ScopeName} by {UserName}: {Outcome} in {DurationMs} ms",
                audit.ToolName, audit.ScopeName, audit.UserName, audit.Outcome, audit.DurationMs);
        }

        await auditWriter.WriteAsync(audit, arguments);
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
        if (context.MatchedPrimitive is not McpServerTool tool)
        {
            return;
        }

        var arguments = context.Params?.Arguments;
        var missingArgument = GetRequiredArgumentNames(tool).FirstOrDefault(name => !HasValue(arguments, name));
        if (missingArgument != null)
        {
            throw new McpProtocolException($"Argument '{missingArgument}' is required.", McpErrorCode.InvalidParams);
        }
    }

    private static IEnumerable<string> GetRequiredArgumentNames(McpServerTool tool)
    {
        var schema = tool.ProtocolTool.InputSchema;

        return schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray().Select(x => x.GetString())
            : [];
    }

    private static bool HasValue(IDictionary<string, JsonElement> arguments, string name)
    {
        return arguments != null && arguments.TryGetValue(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
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
