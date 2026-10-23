using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpApprovalService
{
    Task<McpPendingChange> SubmitAsync(McpPendingChange change);

    Task<McpPendingChange> ApproveAsync(string changeId, string reviewerUserId, string reviewerUserName, string comment);

    Task<McpPendingChange> CompleteAsync(McpPendingChange change, string result, bool succeeded);

    Task<McpPendingChange> RejectAsync(string changeId, string reviewerUserId, string reviewerUserName, string reason);
}
