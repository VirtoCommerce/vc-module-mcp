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
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Diagnostics;
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
    McpToolAvailabilityService toolAvailabilityService,
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
        using var activity = McpDiagnostics.StartToolCall(scope.Name, audit.ToolName, audit.ClientId);
        audit.TraceId = Activity.Current?.TraceId.ToString();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await ExecuteAuditedAsync(next, context, scope, audit, cancellationToken);
            return AddTraceId(result);
        }
        catch (McpProtocolException ex)
        {
            audit.Outcome ??= McpAuditWriter.GetFailureOutcome(ex);
            audit.ErrorMessage = ex.Message;
            throw;
        }
        catch (McpException ex)
        {
            // A refusal of the tool is returned as the tool's own error, so the agent gets the tool's message as it is
            // (the SDK would prefix it with "An error occurred invoking ...") along with the trace id.
            audit.Outcome ??= McpAuditWriter.GetFailureOutcome(ex);
            audit.ErrorMessage = ex.Message;

            return AddTraceId(CreateErrorResult(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MCP tool {ToolName} failed in scope {ScopeName}", audit.ToolName, scope.Name);
            audit.Outcome = McpAuditOutcome.Error;
            audit.ErrorMessage = ex.Message;

            return AddTraceId(CreateErrorResult($"An unexpected error occurred while executing tool '{audit.ToolName}'."));
        }
        finally
        {
            audit.DurationMs = stopwatch.ElapsedMilliseconds;
            audit.PendingChangeId ??= context.GetAuditPendingChangeId();
            await WriteAuditAsync(audit, context);

            if (audit.Outcome != null)
            {
                McpDiagnostics.RecordToolCall(activity, scope.Name, audit.ToolName, audit.Outcome, stopwatch.Elapsed.TotalSeconds, audit.PendingChangeId);
            }
        }
    }

    public virtual async Task AuditRefusalAsync(RequestContext<CallToolRequestParams> context, McpScope scope, McpProtocolException exception)
    {
        var audit = auditWriter.Create(scope.Name, context.Params?.Name, context.User);
        audit.Outcome = await IsForbiddenAsync(context) ? McpAuditOutcome.Forbidden : McpAuditOutcome.Error;
        audit.ErrorMessage = exception.Message;
        audit.TraceId = Activity.Current?.TraceId.ToString();

        await WriteAuditAsync(audit, context);
        McpDiagnostics.RecordToolCall(null, scope.Name, audit.ToolName, audit.Outcome, 0, null);
    }

    internal static bool IsAudited(MessageContext context)
    {
        return context.Items.ContainsKey(AuditedKey);
    }

    public static bool CanBeApprovedIn(McpScope scope)
    {
        return scope?.SupportsApproval == true;
    }

    public static IList<(string ScopeName, string Name)> FindToolsThatCannotBeApproved(IEnumerable<McpServerTool> tools, IMcpScopeRegistry scopeRegistry)
    {
        return tools
            .Where(tool => tool.RequiresApproval())
            .SelectMany(tool =>
            {
                var scopes = tool.GetMcpScopes();
                return scopes.Count == 0
                    ? [(ScopeName: null, tool.ProtocolTool.Name)]
                    : scopes
                        .Where(scopeName => scopeName != McpScopes.Any && !CanBeApprovedIn(scopeRegistry.GetScope(scopeName)))
                        .Select(scopeName => (ScopeName: scopeName, tool.ProtocolTool.Name));
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
        if (await toolAvailabilityService.IsDisabledAsync(scope.Name, audit.ToolName))
        {
            audit.Outcome = McpAuditOutcome.Disabled;
            audit.ErrorMessage = $"Tool '{audit.ToolName}' is disabled by the administrator.";

            return CreateErrorResult(audit.ErrorMessage + " Tell the user that this action is not available now.");
        }

        var tool = context.MatchedPrimitive as McpServerTool;

        if (tool != null && (!toolAuthorizationService.HasMcpPermissions(context.User, tool) ||
            (tool.Metadata.OfType<McpApproverOnlyAttribute>().Any() && !await toolAuthorizationService.IsApproverAsync(context.User, scope))))
        {
            audit.Outcome = McpAuditOutcome.Forbidden;
            audit.ErrorMessage = $"You are not allowed to run tool '{audit.ToolName}'.";

            return CreateErrorResult(audit.ErrorMessage);
        }

        // An invalid argument is reported as a tool error, not as a protocol error, so the model can correct the call.
        var missingArgument = FindMissingRequiredArgument(context);
        if (missingArgument != null)
        {
            audit.Outcome = McpAuditOutcome.Error;
            audit.ErrorMessage = $"Argument '{missingArgument}' is required.";

            return CreateErrorResult(audit.ErrorMessage);
        }

        if (tool != null && tool.RequiresApproval(context.Params?.Arguments) && !IsDryRunCall(tool, context.Params?.Arguments))
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
        if (!CanBeApprovedIn(scope))
        {
            audit.Outcome = McpAuditOutcome.Error;
            throw new McpException($"Tool '{audit.ToolName}' requires approval, which is not available in the '{scope.Name}' scope. The call was not executed.");
        }

        var change = await SubmitForApprovalAsync(next, context, scope, tool, cancellationToken);
        audit.Outcome = McpAuditOutcome.PendingApproval;
        audit.PendingChangeId = change.Id;

        return CreatePendingApprovalResult(change, scope);
    }

    private async Task<bool> IsForbiddenAsync(RequestContext<CallToolRequestParams> context)
    {
        return context.MatchedPrimitive is McpServerTool tool && !await toolAuthorizationService.IsAuthorizedAsync(context.User, tool);
    }

    private async Task WriteAuditAsync(McpAuditEntry audit, RequestContext<CallToolRequestParams> context)
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

        var arguments = audit.Outcome == McpAuditOutcome.Disabled ? null : context.Params?.Arguments;
        var sensitiveArgumentPaths = context.MatchedPrimitive?.GetSensitiveArgumentPaths() ?? [];

        await auditWriter.WriteAsync(audit, arguments, sensitiveArgumentPaths);
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
        change.SensitiveArgumentPaths = [.. tool.GetSensitiveArgumentPaths()];
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

    protected static CallToolResult CreatePendingApprovalResult(McpPendingChange change, McpScope scope)
    {
        var payload = new
        {
            status = "pending_approval",
            change_id = change.Id,
            expires_at = change.ExpirationDate,
            message = $"The change was not applied. It is waiting for approval by a user with the {scope.ApprovalPermission} permission. " +
                "Tell the user the change id; an approver applies it with approve_pending_change. " +
                "The requester can check it with get_pending_change and withdraw it with reject_pending_change.",
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, McpJsonUtilities.DefaultOptions) }],
        };
    }

    protected static string FindMissingRequiredArgument(RequestContext<CallToolRequestParams> context)
    {
        if (context.MatchedPrimitive is not McpServerTool tool)
        {
            return null;
        }

        var arguments = context.Params?.Arguments;
        return GetRequiredArgumentNames(tool).FirstOrDefault(name => !HasValue(arguments, name));
    }

    protected static CallToolResult CreateErrorResult(string message)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
    }

    // The trace id of a failed call lets support find the call in the logs and in the telemetry.
    protected static CallToolResult AddTraceId(CallToolResult result)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (result?.IsError == true && !string.IsNullOrEmpty(traceId))
        {
            result.Meta ??= [];
            result.Meta["trace_id"] = traceId;
        }

        return result;
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
