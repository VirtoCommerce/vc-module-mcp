namespace VirtoCommerce.MCP.Core.Models;

public static class McpJobStatus
{
    public const string Queued = "Queued";

    public const string Running = "Running";

    public const string Succeeded = "Succeeded";

    public const string Failed = "Failed";

    public const string Canceled = "Canceled";

    public static readonly string[] FinalStatuses = [Succeeded, Failed, Canceled];
}
