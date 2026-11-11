namespace VirtoCommerce.MCP.Core.Models;

public class McpJobRequest
{
    // Shown to the agent and on the progress notification in the admin UI.
    public string Title { get; set; }

    // Number of items the job will process, when the tool knows it.
    public long? TotalCount { get; set; }

    // The first affected items, returned to the agent with the job handle. Keep it short: about ten compact rows.
    public object Preview { get; set; }

    // Queue of the background job engine. The default queue when empty.
    public string Queue { get; set; }
}
