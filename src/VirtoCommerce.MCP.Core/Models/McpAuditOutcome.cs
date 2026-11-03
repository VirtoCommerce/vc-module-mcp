namespace VirtoCommerce.MCP.Core.Models;

public static class McpAuditOutcome
{
    public const string Succeeded = "Succeeded";

    public const string Failed = "Failed";

    public const string Forbidden = "Forbidden";

    public const string Error = "Error";

    public const string PendingApproval = "PendingApproval";

    public const string Disabled = "Disabled";

    // The user did not confirm a call of a tool with [McpRequiresConfirmation], so it did not run.
    public const string Declined = "Declined";

    // The call was canceled while it ran: a timeout, or the client went away. It may have changed data partly.
    public const string Canceled = "Canceled";
}
