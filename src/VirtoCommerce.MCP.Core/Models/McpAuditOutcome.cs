namespace VirtoCommerce.MCP.Core.Models;

public static class McpAuditOutcome
{
    public const string Succeeded = "Succeeded";

    public const string Failed = "Failed";

    public const string Forbidden = "Forbidden";

    public const string Error = "Error";

    public const string PendingApproval = "PendingApproval";

    public const string Disabled = "Disabled";

    // A call of a tool with [McpRequiresConfirmation] was not confirmed, so it did not run: the user declined, or the
    // client could not ask the user and the tool refuses such calls (McpConfirmationFallback.Refuse).
    public const string Declined = "Declined";

    // The call was canceled while it ran: a timeout, or the client went away. It may have changed data partly.
    public const string Canceled = "Canceled";
}
