using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.MCP.Core.Models;

// One thing an analysis tool found: what it is, how bad, the evidence and what to do. The same shape in every module, so
// an agent turns the findings of a health check, a conflict check or a readiness check into one action list.
public class McpFinding
{
    // A stable id of the kind of finding, such as "budget-almost-spent" or "memory-high": the agent and the tests refer
    // to it, and it does not change with the wording.
    public string Id { get; set; }

    // McpFindingSeverities: critical, warning or info.
    public string Severity { get; set; }

    // The finding in one sentence a person understands.
    public string Title { get; set; }

    // The figures behind it, with units and the limit they are compared with: "Spent 8,700 of 10,000 USD (87%)".
    public string Evidence { get; set; }

    // What to do about it, naming the tool or the screen that does it when there is one.
    public string Fix { get; set; }

    // The area or the cause the finding belongs to, as the module defines it, such as "policy" or "infrastructure".
    public string Category { get; set; }

    // The object the finding is about, when there is one.
    public string ObjectType { get; set; }

    public string ObjectId { get; set; }

    public string ObjectName { get; set; }
}

public static class McpFindingSeverities
{
    public const string Critical = "critical";
    public const string Warning = "warning";
    public const string Info = "info";

    // Critical first; an unknown severity last.
    public static int GetRank(string severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            Critical => 0,
            Warning => 1,
            Info => 2,
            _ => 3,
        };
    }
}

public static class McpFindingExtensions
{
    // The findings most severe first, in their original order within a severity.
    public static IList<McpFinding> OrderBySeverity(this IEnumerable<McpFinding> findings)
    {
        return (findings ?? []).OrderBy(x => McpFindingSeverities.GetRank(x?.Severity)).ToList();
    }

    // The most severe severity of the findings, or null without findings.
    public static string GetHighestSeverity(this IEnumerable<McpFinding> findings)
    {
        return (findings ?? [])
            .Where(x => x != null)
            .MinBy(x => McpFindingSeverities.GetRank(x.Severity))
            ?.Severity;
    }

    public static int CountBySeverity(this IEnumerable<McpFinding> findings, string severity)
    {
        return (findings ?? []).Count(x => string.Equals(x?.Severity, severity, StringComparison.OrdinalIgnoreCase));
    }
}
