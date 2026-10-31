using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpApprovalService
{
    Task<McpPendingChange> SubmitAsync(McpPendingChange change);

    Task<McpPendingChange> ApproveAsync(string changeId, string reviewerUserId, string reviewerUserName, string comment);

    Task<McpPendingChange> CompleteAsync(McpPendingChange change, string result, bool succeeded);

    Task<McpPendingChange> RejectAsync(string changeId, string reviewerUserId, string reviewerUserName, string reason);

    Task<int> ExpireOverdueAsync(CancellationToken cancellationToken = default);

    // Whether a user may approve a change they requested themselves (setting MCP.Approval.AllowSelfApproval).
    Task<bool> IsSelfApprovalAllowedAsync();

    // Whether an approver of the change's scope may review this change. True by default; a module narrows it, for
    // example to changes requested by users of the approver's own seller in the vendor scope.
    Task<bool> CanReviewAsync(ClaimsPrincipal reviewer, McpPendingChange change);
}
