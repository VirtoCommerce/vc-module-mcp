using System;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Web.Models;

// An MCP scope as the back office shows it, with what the current user may do in it.
public class McpScopeInfo
{
    public string Name { get; set; }

    public string Title { get; set; }

    public string Path { get; set; }

    public bool SupportsApproval { get; set; }

    public string ApprovalPermission { get; set; }

    public bool IsApprover { get; set; }

    public bool IsDisabled { get; set; }
}

// A pending change with what a reviewer needs to decide: the arguments, the dry-run preview and the result, as JSON text.
public class McpPendingChangeDetails
{
    public McpPendingChange Change { get; set; }

    public string Status { get; set; }

    public string ArgumentsJson { get; set; }

    public string PreviewJson { get; set; }

    public string ResultJson { get; set; }

    public bool CanReview { get; set; }

    // Why the current user cannot approve the change, for example because they requested it.
    public string CannotApproveReason { get; set; }
}

public class McpReviewRequest
{
    public string Comment { get; set; }
}

public class McpToolInfo
{
    public string ScopeName { get; set; }

    public string Name { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public bool ReadOnly { get; set; }

    public bool Destructive { get; set; }

    public bool RequiresApproval { get; set; }

    public bool IsEnabled { get; set; }

    // Tool when the tool itself is switched off, Scope when the whole scope is.
    public string DisabledBy { get; set; }
}

public static class McpToolDisabledBy
{
    public const string Tool = "Tool";

    public const string Scope = "Scope";
}

public class McpToolAvailabilityRequest
{
    public string ScopeName { get; set; }

    // A tool name, or * for every tool of the scope.
    public string ToolName { get; set; }

    public bool Enabled { get; set; }
}

public class McpBackOfficeSummary
{
    public int PendingChangeCount { get; set; }

    public int FailedCallCount { get; set; }

    public int DisabledToolCount { get; set; }

    public DateTime Since { get; set; }
}
