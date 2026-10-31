using System.Text.Json.Serialization;

namespace VirtoCommerce.MCP.Core.Models;

// What a bulk tool returns: the id to follow the job with get_job_status, and a preview of the first affected items.
public class McpJobHandle
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("total_count")]
    public long? TotalCount { get; set; }

    [JsonPropertyName("preview")]
    public object Preview { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}
