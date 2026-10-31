using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web.Jobs;
using VirtoCommerce.Platform.Core.Jobs;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpTrackedJobHandlerTests
{
    private readonly Dictionary<string, McpJob> _jobs = [];
    private readonly List<string> _savedStatuses = [];
    private readonly Mock<IJobProgress> _progress = new();

    [Fact]
    public async Task Execute_Should_RunTheModuleHandler_And_RecordItsProgressAndSuccess()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        await handler.Execute(CreatePayload(new CountPayload { Count = 3 }), CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        var job = _jobs["job-1"];
        Assert.Equal(McpJobStatus.Succeeded, job.Status);
        Assert.Equal("engine-1", job.EngineJobId);
        Assert.Equal(3, job.ProcessedCount);
        Assert.Equal(3, job.TotalCount);
        Assert.Equal("3 of 3 done", job.Message);
        Assert.NotNull(job.StartedDate);
        Assert.NotNull(job.FinishedDate);
        Assert.Equal(McpJobStatus.Running, _savedStatuses.First());
        _progress.Verify(x => x.Report(It.IsAny<JobProgressInfo>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Execute_Should_RecordTheFailure_And_RethrowIt()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Execute(CreatePayload(new CountPayload { Count = 1, Fail = true }), CreateContext(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(CountingJob.FailureMessage, exception.Message);
        Assert.Equal(McpJobStatus.Failed, _jobs["job-1"].Status);
        Assert.Equal(CountingJob.FailureMessage, _jobs["job-1"].Message);
        Assert.NotNull(_jobs["job-1"].FinishedDate);
    }

    [Fact]
    public async Task Execute_Should_NotStartAJob_That_WasCanceledInTheQueue()
    {
        // Arrange
        var handler = CreateHandler();
        _jobs["job-1"].Status = McpJobStatus.Canceled;

        // Act
        await handler.Execute(CreatePayload(new CountPayload { Count = 1 }), CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, CountingJob.Executions);
        Assert.Equal(McpJobStatus.Canceled, _jobs["job-1"].Status);
    }

    [Fact]
    public async Task Execute_Should_RecordACanceledJob_When_TheEngineStopsIt()
    {
        // Arrange
        var handler = CreateHandler();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Execute(CreatePayload(new CountPayload { Count = 2, Cancel = true }), CreateContext(), cancellation.Token));

        // Assert
        Assert.Equal(McpJobStatus.Canceled, _jobs["job-1"].Status);
        Assert.NotNull(_jobs["job-1"].FinishedDate);
    }

    [Fact]
    public async Task Execute_Should_SkipTheJob_When_TheMcpJobIsUnknown()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = CreatePayload(new CountPayload { Count = 1 });
        payload.McpJobId = "unknown";

        // Act
        await handler.Execute(payload, CreateContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, CountingJob.Executions);
    }

    private McpTrackedJobHandler CreateHandler()
    {
        CountingJob.Executions = 0;
        _jobs["job-1"] = new McpJob { Id = "job-1", ScopeName = "admin", ToolName = "count", Status = McpJobStatus.Queued };

        var jobService = new Mock<IMcpJobService>();
        jobService
            .Setup(x => x.GetAsync(It.IsAny<IList<string>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((IList<string> ids, string _, bool _) => ids.Where(_jobs.ContainsKey).Select(id => _jobs[id]).ToList());
        jobService
            .Setup(x => x.SaveChangesAsync(It.IsAny<IList<McpJob>>()))
            .Callback<IList<McpJob>>(jobs => _savedStatuses.AddRange(jobs.Select(x => x.Status)))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddTransient<CountingJob>().BuildServiceProvider();

        return new McpTrackedJobHandler(services, jobService.Object, new TestTimeProvider(), NullLogger<McpTrackedJobHandler>.Instance);
    }

    private IJobExecutionContext CreateContext()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(x => x.JobId).Returns("engine-1");
        context.SetupGet(x => x.Progress).Returns(_progress.Object);
        context.SetupGet(x => x.Headers).Returns(new Dictionary<string, string>());

        return context.Object;
    }

    private static McpTrackedJobPayload CreatePayload(CountPayload payload)
    {
        return new McpTrackedJobPayload
        {
            McpJobId = "job-1",
            HandlerType = typeof(CountingJob).AssemblyQualifiedName,
            HandlerPayloadType = typeof(CountPayload).AssemblyQualifiedName,
            PayloadType = typeof(CountPayload).AssemblyQualifiedName,
            Payload = JsonConvert.SerializeObject(payload),
        };
    }

    public class CountPayload
    {
        public int Count { get; set; }

        public bool Fail { get; set; }

        public bool Cancel { get; set; }
    }

    public class CountingJob : IBackgroundJobHandler<CountPayload>
    {
        public const string FailureMessage = "The coupon store is not available.";

        public static int Executions { get; set; }

        public async Task Execute(CountPayload payload, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executions++;

            if (payload.Fail)
            {
                throw new InvalidOperationException(FailureMessage);
            }

            if (payload.Cancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var i = 1; i <= payload.Count; i++)
            {
                await context.Progress.Report(new JobProgressInfo { ProcessedCount = i, TotalCount = payload.Count, Message = $"{i} of {payload.Count} done" }, cancellationToken);
            }
        }
    }
}
