using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
using VirtoCommerce.MCP.Web.Middleware;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Filters;

public class McpCallToolFilter(
    IMcpApprovalService approvalService,
    McpToolAuthorizationService toolAuthorizationService,
    McpToolAvailabilityService toolAvailabilityService,
    McpAuditWriter auditWriter,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<McpCallToolFilter> logger)
{
    private const string AuditedKey = "VirtoCommerce.MCP.Audit.Written";
    private const string ConfirmationKey = "confirmation";
    private const string ConfirmField = "confirm";
    private const string ConfirmationStatePrefix = "mcp-confirm:";
    private const int MaxConfirmationArguments = 10;
    private const int MaxConfirmationValueLength = 120;
    private const string ConfirmationProtectorPurpose = "VirtoCommerce.MCP.Confirmation.v1";
    private const string UsedConfirmationKeyPrefix = "VirtoCommerce.MCP.Confirmation.Used:";

    // How long the user has to answer the question
    protected static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(10);

    private static readonly Regex _placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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
        catch (InputRequiredException)
        {
            // The call waits for the user's answer: the client asks the user and repeats the call, which is audited.
            throw;
        }
        catch (OperationCanceledException)
        {
            // A timeout or a client that went away may leave a partial change behind, so the call is still audited.
            audit.Outcome = McpAuditOutcome.Canceled;
            audit.ErrorMessage = "The call was canceled before it finished.";
            throw;
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
        catch (Exception ex)
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
        var tool = context.MatchedPrimitive as McpServerTool;

        var refusal = await RefuseAsync(context, scope, tool, audit);
        if (refusal != null)
        {
            return refusal;
        }

        var declined = await ConfirmAsync(context, scope, tool, audit);
        if (declined != null)
        {
            return declined;
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

    // The error result of a call that must not run, with its audit outcome, or null when the call may run.
    private async Task<CallToolResult> RefuseAsync(RequestContext<CallToolRequestParams> context, McpScope scope, McpServerTool tool, McpAuditEntry audit)
    {
        if (await toolAvailabilityService.IsDisabledAsync(scope.Name, audit.ToolName))
        {
            audit.Outcome = McpAuditOutcome.Disabled;
            audit.ErrorMessage = $"Tool '{audit.ToolName}' is disabled by the administrator.";

            return CreateErrorResult(audit.ErrorMessage + " Tell the user that this action is not available now.");
        }

        // The scope's middleware challenges such a call before it gets here; this holds when the request named the tool
        // in a way that middleware did not read.
        if (tool != null && context.User?.Identity?.IsAuthenticated != true && McpScopeAuthenticationMiddleware.RequiresUser(tool))
        {
            audit.Outcome = McpAuditOutcome.Forbidden;
            audit.ErrorMessage = $"Sign in to use tool '{audit.ToolName}'.";

            return CreateErrorResult(audit.ErrorMessage);
        }

        if (tool != null && !await MayRunAsync(context, scope, tool))
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

        return null;
    }

    // [McpPermission] of the tool, and the scope's approver permission for [McpApproverOnly] tools
    private async Task<bool> MayRunAsync(RequestContext<CallToolRequestParams> context, McpScope scope, McpServerTool tool)
    {
        if (!toolAuthorizationService.HasMcpPermissions(context.User, tool))
        {
            return false;
        }

        return !tool.Metadata.OfType<McpApproverOnlyAttribute>().Any() || await toolAuthorizationService.IsApproverAsync(context.User, scope);
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

        if (FindDryRunParameter(tool) == null)
        {
            audit.Outcome = McpAuditOutcome.Error;
            throw new McpException($"Tool '{audit.ToolName}' requires approval but has no dry run, so its change cannot be previewed for the approver. The call was not executed.");
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

    // The confirmation of a tool with [McpRequiresConfirmation], except for its dry runs, which change nothing.
    private async Task<CallToolResult> ConfirmAsync(RequestContext<CallToolRequestParams> context, McpScope scope, McpServerTool tool, McpAuditEntry audit)
    {
        var confirmation = tool?.GetConfirmation();
        if (confirmation == null || IsDryRunCall(tool, context.Params?.Arguments))
        {
            return null;
        }

        if (!CanAskUser(context.Server))
        {
            return confirmation.Fallback == McpConfirmationFallback.Refuse ? RefuseUnconfirmable(tool, audit) : null;
        }

        return ConfirmOrAsk(context, tool, audit, await GetConfirmationDetailsAsync(context, scope, tool));
    }

    // A tool that a person must confirm, called from a client that cannot ask: the call does not run.
    protected virtual CallToolResult RefuseUnconfirmable(McpServerTool tool, McpAuditEntry audit)
    {
        audit.Outcome = McpAuditOutcome.Declined;
        audit.ErrorMessage = "The client cannot ask the user to confirm the call, and the tool does not run without it.";

        var where = tool.ProtocolTool.Name == ModuleConstants.ToolNames.ApprovePendingChange
            ? "Ask the user to approve the change in the back office (MCP > Pending changes)"
            : "Ask the user to do it in the back office";

        return CreateErrorResult($"'{tool.ProtocolTool.Name}' runs only after the user confirms it, and this MCP client cannot ask the user, " +
            $"so nothing was done. {where}, or to use an MCP client that supports elicitation.");
    }

    // What the user confirms besides the call itself: for approve_pending_change, the change it applies, since the
    // change id alone tells the user nothing. Only a change the user may review in this scope is described; for any
    // other the tool itself answers that it was not found.
    protected virtual async Task<string> GetConfirmationDetailsAsync(RequestContext<CallToolRequestParams> context, McpScope scope, McpServerTool tool)
    {
        if (tool.ProtocolTool.Name != ModuleConstants.ToolNames.ApprovePendingChange ||
            context.Params?.Arguments?.TryGetValue("change_id", out var changeId) != true || changeId.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var change = await context.Services.GetRequiredService<IMcpPendingChangeService>().GetByIdAsync(changeId.GetString());
        if (change == null || !string.Equals(change.ScopeName, scope.Name, StringComparison.OrdinalIgnoreCase) ||
            !await approvalService.CanReviewAsync(context.User, change))
        {
            return null;
        }

        var lines = new List<string> { $"Tool: {change.ToolName}", $"Requested by: {change.RequestedByUserName}" };
        lines.AddRange(ToShortTexts(change.GetMaskedArguments()).Take(MaxConfirmationArguments).Select(x => $"{x.Key}: {x.Value}"));

        return string.Join(Environment.NewLine, lines);
    }

    // [McpRequiresConfirmation]: null when the user confirmed the call, or when the client cannot ask the user, so the
    // call runs; the error result when the user declined. Otherwise the user is asked: the call ends with an
    // input-required result, and the client repeats it with the user's answer and the state of the question.
    protected virtual CallToolResult ConfirmOrAsk(RequestContext<CallToolRequestParams> context, McpServerTool tool, McpAuditEntry audit, string details = null)
    {
        if (!CanAskUser(context.Server))
        {
            return null;
        }

        var arguments = context.Params?.Arguments;

        if (context.Params?.InputResponses?.TryGetValue(ConfirmationKey, out var response) == true &&
            TryUseConfirmationState(context, tool, arguments, context.Params.RequestState))
        {
            if (IsConfirmed(response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)))
            {
                return null;
            }

            audit.Outcome = McpAuditOutcome.Declined;
            audit.ErrorMessage = "The user did not confirm the call.";

            return CreateErrorResult("The user did not confirm the call, so nothing was done. Ask the user what to do instead.");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest> { [ConfirmationKey] = InputRequest.ForElicitation(CreateConfirmationRequest(tool, arguments, details)) },
            CreateConfirmationState(context, tool, arguments));
    }

    // The client asks the user in the chat: elicitation with forms, over multi round-trip requests, which a stateless
    // server needs because it cannot send the client a request of its own.
    protected static bool CanAskUser(McpServer server)
    {
        return server is { IsMrtrSupported: true } && server.ClientCapabilities?.Elicitation?.Form != null;
    }

    protected virtual ElicitRequestParams CreateConfirmationRequest(McpServerTool tool, IDictionary<string, JsonElement> arguments, string details = null)
    {
        var values = GetMaskedArguments(tool, arguments);
        var template = tool.GetConfirmation()?.Message;
        var message = string.IsNullOrEmpty(template)
            ? DescribeCall(tool, values)
            : _placeholder.Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

        if (!string.IsNullOrEmpty(details))
        {
            message += Environment.NewLine + details;
        }

        return new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [ConfirmField] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Confirm",
                        Description = "Run this action now.",
                        Default = true,
                    },
                },
            },
        };
    }

    // Accepted, and the confirm box was not unticked
    protected static bool IsConfirmed(ElicitResult answer)
    {
        if (answer?.IsAccepted != true)
        {
            return false;
        }

        return answer.Content?.TryGetValue(ConfirmField, out var confirm) != true || confirm.ValueKind != JsonValueKind.False;
    }

    // The state of a question ties the answer to it: the same user, tool and arguments, within the lifetime, once. The
    // server protects it (data protection: signed and encrypted), so a client cannot make up the state of a question it
    // was never asked and attach an answer to its first call.
    protected virtual string CreateConfirmationState(RequestContext<CallToolRequestParams> context, McpServerTool tool, IDictionary<string, JsonElement> arguments)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = string.Join('\n', nonce, GetConfirmedCall(context, tool, arguments));

        return ConfirmationStatePrefix + GetConfirmationProtector().Protect(payload, ConfirmationLifetime);
    }

    // Whether the state is one this server issued for this call and has not been used yet. Its nonce is remembered until
    // the state expires; on a cluster that is per instance, so the lifetime is what bounds a repeat on another one.
    protected virtual bool TryUseConfirmationState(RequestContext<CallToolRequestParams> context, McpServerTool tool, IDictionary<string, JsonElement> arguments, string state)
    {
        if (state?.StartsWith(ConfirmationStatePrefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        string payload;
        DateTimeOffset expiration;
        try
        {
            payload = GetConfirmationProtector().Unprotect(state[ConfirmationStatePrefix.Length..], out expiration);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = payload.Split('\n', 2);
        if (parts.Length != 2 || !string.Equals(parts[1], GetConfirmedCall(context, tool, arguments), StringComparison.Ordinal))
        {
            return false;
        }

        var cache = context.Services.GetRequiredService<IMemoryCache>();
        var usedKey = UsedConfirmationKeyPrefix + parts[0];
        if (cache.TryGetValue(usedKey, out _))
        {
            return false;
        }

        cache.Set(usedKey, true, expiration);
        return true;
    }

    private ITimeLimitedDataProtector GetConfirmationProtector()
    {
        return dataProtectionProvider.CreateProtector(ConfirmationProtectorPurpose).ToTimeLimitedDataProtector();
    }

    // The user, the tool and a hash of the arguments of the call the user confirms
    private static string GetConfirmedCall(RequestContext<CallToolRequestParams> context, McpServerTool tool, IDictionary<string, JsonElement> arguments)
    {
        var argumentsJson = JsonSerializer.Serialize(arguments ?? new Dictionary<string, JsonElement>());
        var argumentsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(argumentsJson)));

        return string.Join('\n', context.User?.GetUserId() ?? string.Empty, tool.ProtocolTool.Name, argumentsHash);
    }

    private static string DescribeCall(McpServerTool tool, IDictionary<string, string> values)
    {
        var title = tool.ProtocolTool.Title ?? tool.ProtocolTool.Annotations?.Title ?? tool.ProtocolTool.Name;
        var lines = values
            .Take(MaxConfirmationArguments)
            .Select(x => $"{x.Key}: {x.Value}");

        return string.Join(Environment.NewLine, new[] { $"Run \"{title}\"?" }.Concat(lines));
    }

    // The arguments as short texts, with [McpSensitive] values masked
    private static Dictionary<string, string> GetMaskedArguments(McpServerTool tool, IDictionary<string, JsonElement> arguments)
    {
        return arguments is { Count: > 0 }
            ? ToShortTexts(McpSensitiveArguments.Mask(JsonSerializer.Serialize(arguments), tool.GetSensitiveArgumentPaths()))
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // The values of a JSON object of arguments as short texts
    private static Dictionary<string, string> ToShortTexts(string argumentsJson)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(argumentsJson) || JsonNode.Parse(argumentsJson) is not JsonObject values)
        {
            return result;
        }

        foreach (var (name, value) in values)
        {
            var text = value is JsonValue scalar && scalar.TryGetValue<string>(out var str) ? str : value?.ToJsonString() ?? "null";
            result[name] = text.Length > MaxConfirmationValueLength ? text[..MaxConfirmationValueLength] + "…" : text;
        }

        return result;
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
        return tool.FindDryRunParameter();
    }

    protected static string GetText(CallToolResult result)
    {
        return string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(x => x.Text));
    }
}
