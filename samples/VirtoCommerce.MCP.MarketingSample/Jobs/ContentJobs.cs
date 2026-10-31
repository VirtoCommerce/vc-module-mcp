using System;
using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.Platform.Core.Jobs;

namespace VirtoCommerce.MCP.MarketingSample.Jobs;

public class CloneContentCampaignPayload
{
    public string[] PublicationIds { get; set; } = [];

    public string TargetStoreId { get; set; }

    public int ShiftDays { get; set; }
}

// Copies publications to another store, matching placeholders by name and shifting the dates.
public class CloneContentCampaignJob(ContentDemoStore content) : IBackgroundJobHandler<CloneContentCampaignPayload>
{
    public async Task Execute(CloneContentCampaignPayload payload, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var done = 0;
        var copied = 0;

        foreach (var id in payload.PublicationIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = content.GetPublication(id);
            if (source != null && content.GetPlaceholder(payload.TargetStoreId, source.Placeholder) != null)
            {
                var copy = source.Clone();
                copy.Id = content.NewPublicationId();
                copy.StoreId = payload.TargetStoreId;
                copy.StartDate = source.StartDate.AddDays(payload.ShiftDays);
                copy.EndDate = source.EndDate.AddDays(payload.ShiftDays);
                content.SavePublication(copy);
                copied++;
            }

            done++;
            await context.Progress.Report(new JobProgressInfo
            {
                Message = $"{copied} of {payload.PublicationIds.Length} publications copied to {payload.TargetStoreId}.",
                ProcessedCount = done,
                TotalCount = payload.PublicationIds.Length,
            }, cancellationToken);

            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        }
    }
}
