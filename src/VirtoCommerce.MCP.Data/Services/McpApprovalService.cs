using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Diagnostics;
using VirtoCommerce.MCP.Core.Events;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.Platform.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Data.Services;

public class McpApprovalService(
    Func<IMcpRepository> repositoryFactory,
    IMcpPendingChangeService pendingChangeService,
    ISettingsManager settingsManager,
    IEventPublisher eventPublisher,
    TimeProvider timeProvider)
    : IMcpApprovalService
{
    private const int BatchSize = 1000;

    public virtual Task<McpPendingChange> SubmitAsync(McpPendingChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return SubmitPendingAsync(change);
    }

    private async Task<McpPendingChange> SubmitPendingAsync(McpPendingChange change)
    {
        var lifetimeHours = await settingsManager.GetValueAsync<int>(ModuleConstants.Settings.Approval.PendingChangeLifetimeHours);

        change.Id = null;
        change.Status = McpPendingChangeStatus.Pending;
        change.ExpirationDate = timeProvider.GetUtcNow().UtcDateTime.AddHours(lifetimeHours);
        change.ReviewedByUserId = null;
        change.ReviewedByUserName = null;
        change.ReviewedDate = null;
        change.ReviewComment = null;
        change.Result = null;

        await pendingChangeService.SaveChangesAsync([change]);
        McpDiagnostics.RecordApprovalDecision(change.ScopeName, change.ToolName, "submitted");

        return change;
    }

    public virtual async Task<McpPendingChange> ApproveAsync(string changeId, string reviewerUserId, string reviewerUserName, string comment)
    {
        var change = await GetPendingAsync(changeId);

        if (!await IsSelfApprovalAllowedAsync() && !string.IsNullOrEmpty(change.RequestedByUserId) &&
            string.Equals(change.RequestedByUserId, reviewerUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Pending change '{changeId}' was requested by you and must be approved by another user.");
        }

        return await TransitionAsync(change, x =>
        {
            x.Status = McpPendingChangeStatus.Approved;
            x.ReviewedByUserId = reviewerUserId;
            x.ReviewedByUserName = reviewerUserName;
            x.ReviewedDate = timeProvider.GetUtcNow().UtcDateTime;
            x.ReviewComment = comment;
        });
    }

    public virtual Task<McpPendingChange> CompleteAsync(McpPendingChange change, string result, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Status != McpPendingChangeStatus.Approved)
        {
            throw new McpException($"Pending change '{change.Id}' is {change.Status?.ToLowerInvariant()}, not approved, and cannot be completed.");
        }

        return TransitionAsync(change, x =>
        {
            x.Status = succeeded ? McpPendingChangeStatus.Applied : McpPendingChangeStatus.Failed;
            x.Result = result;
        });
    }

    public virtual async Task<McpPendingChange> RejectAsync(string changeId, string reviewerUserId, string reviewerUserName, string reason)
    {
        var change = await GetPendingAsync(changeId);

        return await TransitionAsync(change, x =>
        {
            x.Status = McpPendingChangeStatus.Rejected;
            x.ReviewedByUserId = reviewerUserId;
            x.ReviewedByUserName = reviewerUserName;
            x.ReviewedDate = timeProvider.GetUtcNow().UtcDateTime;
            x.ReviewComment = reason;
        });
    }

    public virtual Task<bool> IsSelfApprovalAllowedAsync()
    {
        return settingsManager.GetValueAsync<bool>(ModuleConstants.Settings.Approval.AllowSelfApproval);
    }

    public virtual Task<bool> CanReviewAsync(System.Security.Claims.ClaimsPrincipal reviewer, McpPendingChange change)
    {
        return Task.FromResult(true);
    }

    public virtual async Task<int> ExpireOverdueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expired = 0;
        List<string> ids;

        do
        {
            using (var repository = repositoryFactory())
            {
                ids = await repository.PendingChanges
                    .Where(x => x.Status == McpPendingChangeStatus.Pending && x.ExpirationDate <= now)
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);
            }

            foreach (var id in ids)
            {
                expired += await TryExpireAsync(id, now) ? 1 : 0;
            }
        }
        while (ids.Count == BatchSize);

        return expired;
    }

    protected virtual async Task<McpPendingChange> GetPendingAsync(string changeId)
    {
        var change = await LoadAsync(changeId) ?? throw new McpException($"Pending change '{changeId}' was not found.");

        if (change.IsExpired(timeProvider.GetUtcNow().UtcDateTime))
        {
            await TransitionAsync(change, x => x.Status = McpPendingChangeStatus.Expired);

            throw new McpException($"Pending change '{changeId}' expired on {change.ExpirationDate:u} and can no longer be reviewed.");
        }

        if (change.Status != McpPendingChangeStatus.Pending)
        {
            throw new McpException($"Pending change '{changeId}' is already {change.Status.ToLowerInvariant()}.");
        }

        return change;
    }

    protected virtual async Task<McpPendingChange> TransitionAsync(McpPendingChange change, Action<McpPendingChange> apply)
    {
        var updated = (McpPendingChange)change.Clone();
        apply(updated);
        updated.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;

        if (updated.IsFinal())
        {
            updated.Arguments = updated.GetMaskedArguments();
        }

        var changedEntries = new[] { new GenericChangedEntry<McpPendingChange>(updated, change, EntryState.Modified) };
        await eventPublisher.Publish(new McpPendingChangeChangingEvent(changedEntries));

        using (var repository = repositoryFactory())
        {
            var affected = await repository.PendingChanges
                .Where(x => x.Id == change.Id && x.Status == change.Status)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(e => e.Arguments, updated.Arguments)
                    .SetProperty(e => e.Status, updated.Status)
                    .SetProperty(e => e.ReviewedByUserId, updated.ReviewedByUserId)
                    .SetProperty(e => e.ReviewedByUserName, updated.ReviewedByUserName)
                    .SetProperty(e => e.ReviewedDate, updated.ReviewedDate)
                    .SetProperty(e => e.ReviewComment, updated.ReviewComment)
                    .SetProperty(e => e.Result, updated.Result)
                    .SetProperty(e => e.ModifiedDate, updated.ModifiedDate));

            if (affected == 0)
            {
                throw new McpException($"Pending change '{change.Id}' was reviewed by someone else in the meantime.");
            }
        }

        GenericCachingRegion<McpPendingChange>.ExpireTokenForKey(change.Id);
        GenericSearchCachingRegion<McpPendingChange>.ExpireRegion();

        await eventPublisher.Publish(new McpPendingChangeChangedEvent(changedEntries));
        McpDiagnostics.RecordApprovalDecision(updated.ScopeName, updated.ToolName, updated.Status.ToLowerInvariant());

        return updated;
    }

    private async Task<bool> TryExpireAsync(string changeId, DateTime now)
    {
        var change = await LoadAsync(changeId);
        if (change?.IsExpired(now) != true)
        {
            return false;
        }

        try
        {
            await TransitionAsync(change, x => x.Status = McpPendingChangeStatus.Expired);
            return true;
        }
        catch (McpException)
        {
            return false;
        }
    }

    protected virtual async Task<McpPendingChange> LoadAsync(string changeId)
    {
        if (string.IsNullOrEmpty(changeId))
        {
            return null;
        }

        using var repository = repositoryFactory();
        var entity = await repository.PendingChanges.AsNoTracking().FirstOrDefaultAsync(x => x.Id == changeId);

        return entity?.ToModel(AbstractTypeFactory<McpPendingChange>.TryCreateInstance());
    }
}
