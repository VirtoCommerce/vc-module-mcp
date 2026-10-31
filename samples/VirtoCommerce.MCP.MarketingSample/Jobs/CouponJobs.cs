using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.Platform.Core.Jobs;

namespace VirtoCommerce.MCP.MarketingSample.Jobs;

public class GenerateCouponsPayload
{
    public string PromotionId { get; set; }

    public string Prefix { get; set; }

    public int Count { get; set; }

    public int? MaxUses { get; set; }

    public DateTime? ExpirationDate { get; set; }

    // The same seed in the dry run and in the job, so the preview shows the codes the job creates.
    public int Seed { get; set; }
}

public class DeactivateStaleCouponsPayload
{
    public string[] StoreIds { get; set; } = [];
}

// Ordinary platform background job handlers: IMcpJobDispatcher runs them and turns their progress into the
// status of the MCP job, which the agent follows with get_job_status.
public class GenerateCouponsJob(MarketingDemoStore store) : IBackgroundJobHandler<GenerateCouponsPayload>
{
    private const int BatchSize = 500;

    public static IEnumerable<string> GetCodes(MarketingDemoStore store, GenerateCouponsPayload payload)
    {
        return MarketingDemoSeed.GenerateCodes(payload.Prefix, payload.Count * 2, payload.Seed)
            .Where(code => !store.CouponExists(code))
            .Take(payload.Count);
    }

    public async Task Execute(GenerateCouponsPayload payload, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var codes = GetCodes(store, payload).ToList();
        var created = 0;

        foreach (var batch in codes.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            store.AddCoupons(batch.Select(code => new DemoCoupon
            {
                Code = code,
                PromotionId = payload.PromotionId,
                MaxUses = payload.MaxUses,
                ExpirationDate = payload.ExpirationDate,
                CreatedDate = store.Now,
            }));
            created += batch.Length;

            await context.Progress.Report(new JobProgressInfo
            {
                Message = $"{created:N0} of {codes.Count:N0} coupons created for promotion {payload.PromotionId}.",
                ProcessedCount = created,
                TotalCount = codes.Count,
            }, cancellationToken);

            // A real coupon import takes time; the pause makes the progress visible in the demo.
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }
    }
}

public class DeactivateStaleCouponsJob(MarketingDemoStore store) : IBackgroundJobHandler<DeactivateStaleCouponsPayload>
{
    private const int BatchSize = 50;

    public static IList<DemoCoupon> FindStaleCoupons(MarketingDemoStore store, IEnumerable<string> storeIds)
    {
        var now = store.Now;
        var expired = store.GetPromotions()
            .Where(x => storeIds.Contains(x.StoreId) && x.GetStatus(now) == PromotionStatus.Expired)
            .Select(x => x.Id)
            .ToHashSet();

        return store.GetCoupons()
            .Where(x => expired.Contains(x.PromotionId) && x.IsActive && !x.IsUsedUp)
            .ToList();
    }

    public async Task Execute(DeactivateStaleCouponsPayload payload, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var coupons = FindStaleCoupons(store, payload.StoreIds);
        var done = 0;

        foreach (var batch in coupons.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            store.DeactivateCoupons(batch.Select(x => x.Code));
            done += batch.Length;

            await context.Progress.Report(new JobProgressInfo
            {
                Message = $"{done} of {coupons.Count} stale coupons deactivated.",
                ProcessedCount = done,
                TotalCount = coupons.Count,
            }, cancellationToken);

            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        }
    }
}
