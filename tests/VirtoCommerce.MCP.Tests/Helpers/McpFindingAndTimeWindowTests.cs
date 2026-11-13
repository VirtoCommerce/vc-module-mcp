using System;
using System.Linq;
using ModelContextProtocol;
using VirtoCommerce.MCP.Core.Models;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpFindingTests
{
    private static readonly McpFinding[] _findings =
    [
        new() { Id = "a", Severity = McpFindingSeverities.Info },
        new() { Id = "b", Severity = McpFindingSeverities.Critical },
        new() { Id = "c", Severity = McpFindingSeverities.Warning },
        new() { Id = "d", Severity = McpFindingSeverities.Critical },
    ];

    [Fact]
    public void OrderBySeverity_PutsCriticalFirst_AndKeepsTheOrderWithinASeverity()
    {
        Assert.Equal(["b", "d", "c", "a"], _findings.OrderBySeverity().Select(x => x.Id));
    }

    [Fact]
    public void GetHighestSeverity_IsTheMostSevere()
    {
        Assert.Equal(McpFindingSeverities.Critical, _findings.GetHighestSeverity());
        Assert.Null(Array.Empty<McpFinding>().GetHighestSeverity());
    }

    [Fact]
    public void CountBySeverity_CountsOneSeverity()
    {
        Assert.Equal(2, _findings.CountBySeverity(McpFindingSeverities.Critical));
    }

    [Fact]
    public void GetRank_PutsAnUnknownSeverityLast()
    {
        Assert.True(McpFindingSeverities.GetRank("severe") > McpFindingSeverities.GetRank(McpFindingSeverities.Info));
    }
}

[Trait("Category", "Unit")]
public class McpTimeWindowTests
{
    private static readonly DateTime _now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    private readonly FixedTime _time = new(_now);

    [Fact]
    public void LastHours_DefaultsWhenTheAgentGivesNothing()
    {
        var window = McpTimeWindow.LastHours(null, _time);

        Assert.Equal(_now.AddHours(-24), window.From);
        Assert.Equal(_now, window.To);
        Assert.Equal(24, window.Hours);
        Assert.False(window.WasCapped);
    }

    [Fact]
    public void LastHours_IsCapped_AndSaysSo()
    {
        var window = McpTimeWindow.LastHours(10_000, _time, maxHours: 72);

        Assert.Equal(72, window.Hours);
        Assert.True(window.WasCapped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void LastHours_RefusesLessThanOne_WithAMessageTheAgentCanPassOn(int hours)
    {
        var exception = Assert.Throws<McpException>(() => McpTimeWindow.LastHours(hours, _time));

        Assert.Contains("'hours' must be at least 1", exception.Message);
    }

    [Fact]
    public void LastDays_CountsDays()
    {
        Assert.Equal(_now.AddDays(-30), McpTimeWindow.LastDays(30, _time).From);
    }

    [Fact]
    public void Since_StartsAtTheMoment()
    {
        var window = McpTimeWindow.Since(_now.AddHours(-5), _time, TimeSpan.FromDays(1), TimeSpan.FromDays(7));

        Assert.Equal(5, window.Hours);
        Assert.False(window.WasCapped);
    }

    [Fact]
    public void Since_ReadsAMomentWithoutAKindAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(_now.AddHours(-2), DateTimeKind.Unspecified);

        var window = McpTimeWindow.Since(unspecified, _time, TimeSpan.FromDays(1), TimeSpan.FromDays(7));

        Assert.Equal(_now.AddHours(-2), window.From);
        Assert.Equal(DateTimeKind.Utc, window.From.Kind);
    }

    [Fact]
    public void Since_IsCappedAtTheMaximum()
    {
        var window = McpTimeWindow.Since(_now.AddYears(-1), _time, TimeSpan.FromDays(1), TimeSpan.FromDays(7));

        Assert.Equal(_now.AddDays(-7), window.From);
        Assert.True(window.WasCapped);
    }

    [Fact]
    public void Since_DefaultsWithoutAMoment()
    {
        Assert.Equal(_now.AddDays(-1), McpTimeWindow.Since(null, _time, TimeSpan.FromDays(1), TimeSpan.FromDays(7)).From);
    }

    [Fact]
    public void Since_RefusesTheFuture()
    {
        Assert.Throws<McpException>(() => McpTimeWindow.Since(_now.AddHours(1), _time, TimeSpan.FromDays(1), TimeSpan.FromDays(7)));
    }

    [Fact]
    public void Contains_IncludesBothEnds()
    {
        var window = McpTimeWindow.LastHours(1, _time);

        Assert.True(window.Contains(_now));
        Assert.True(window.Contains(_now.AddHours(-1)));
        Assert.False(window.Contains(_now.AddHours(-2)));
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
