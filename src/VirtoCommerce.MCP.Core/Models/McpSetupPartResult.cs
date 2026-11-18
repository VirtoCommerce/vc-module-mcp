using System.Collections.Generic;

namespace VirtoCommerce.MCP.Core.Models;

// What a setup changes, or changed, in one part, and how it went.
public class McpSetupPartResult
{
    public string Part { get; set; }

    public McpSetupPartStatus Status { get; set; }

    public IList<McpFieldChange> Changes { get; set; } = [];

    public IList<string> Warnings { get; set; } = [];

    // Why the part failed to apply, without the exception's text, which goes to the log.
    public string Error { get; set; }
}
