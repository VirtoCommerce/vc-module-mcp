using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace VirtoCommerce.MCP.Core.Diagnostics;

// OpenTelemetry signals of the MCP infrastructure. The module does not reference OpenTelemetry: a deployment collects
// them by adding "VirtoCommerce.MCP" to OpenTelemetry:Sources and OpenTelemetry:Meters of the OpenTelemetry module.
// The audit log stays the business record of who did what; traces and metrics are for operations.
public static class McpDiagnostics
{
    public const string ActivitySourceName = "VirtoCommerce.MCP";
    public const string MeterName = "VirtoCommerce.MCP";

    public const string ScopeTag = "vc.mcp.scope";
    public const string ToolTag = "gen_ai.tool.name";
    public const string MethodTag = "mcp.method.name";
    public const string OutcomeTag = "vc.mcp.outcome";
    public const string ClientTag = "vc.mcp.client_id";
    public const string PendingChangeTag = "vc.mcp.pending_change_id";
    public const string DecisionTag = "vc.mcp.approval.decision";
    public const string JobStatusTag = "vc.mcp.job.status";
    public const string ReviewChannelTag = "vc.mcp.review.channel";

    public static readonly string ModuleVersion =
        typeof(McpDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(McpDiagnostics).Assembly.GetName().Version?.ToString();

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ModuleVersion);

    public static readonly Meter Meter = new(MeterName, ModuleVersion);

    private static readonly Counter<long> _toolCalls = Meter.CreateCounter<long>(
        "vc.mcp.tool.calls", unit: "{call}", description: "MCP tool calls by scope, tool and outcome.");

    private static readonly Histogram<double> _toolDuration = Meter.CreateHistogram<double>(
        "vc.mcp.tool.duration", unit: "s", description: "Duration of MCP tool calls.");

    private static readonly Counter<long> _approvals = Meter.CreateCounter<long>(
        "vc.mcp.approval.decisions", unit: "{change}", description: "Changes submitted for approval and their reviews.");

    private static readonly Counter<long> _jobs = Meter.CreateCounter<long>(
        "vc.mcp.jobs", unit: "{job}", description: "MCP jobs that reached a final status.");

    public static Activity StartToolCall(string scopeName, string toolName, string clientId)
    {
        var activity = ActivitySource.StartActivity($"MCP tool {toolName}", ActivityKind.Internal);
        activity?.SetTag(MethodTag, "tools/call");
        activity?.SetTag(ScopeTag, scopeName);
        activity?.SetTag(ToolTag, toolName);
        activity?.SetTag(ClientTag, clientId);

        return activity;
    }

    public static Activity StartReview(string scopeName, string toolName, string decision, string pendingChangeId, string channel)
    {
        var activity = ActivitySource.StartActivity($"MCP review {decision} {toolName}", ActivityKind.Internal);
        activity?.SetTag(ScopeTag, scopeName);
        activity?.SetTag(ToolTag, toolName);
        activity?.SetTag(DecisionTag, decision);
        activity?.SetTag(PendingChangeTag, pendingChangeId);
        activity?.SetTag(ReviewChannelTag, channel);

        return activity;
    }

    public static void RecordToolCall(Activity activity, string scopeName, string toolName, string outcome, double durationSeconds, string pendingChangeId)
    {
        if (activity != null)
        {
            activity.SetTag(OutcomeTag, outcome);
            activity.SetTag(PendingChangeTag, pendingChangeId);

            if (outcome is Models.McpAuditOutcome.Error or Models.McpAuditOutcome.Failed)
            {
                activity.SetStatus(ActivityStatusCode.Error, outcome);
            }
        }

        var tags = new TagList
        {
            { ScopeTag, scopeName },
            { ToolTag, toolName },
            { OutcomeTag, outcome },
        };

        _toolCalls.Add(1, tags);
        _toolDuration.Record(durationSeconds, tags);
    }

    public static void RecordApprovalDecision(string scopeName, string toolName, string decision)
    {
        _approvals.Add(1, new TagList
        {
            { ScopeTag, scopeName },
            { ToolTag, toolName },
            { DecisionTag, decision },
        });
    }

    public static void RecordJob(string status)
    {
        _jobs.Add(1, new TagList { { JobStatusTag, status } });
    }
}
