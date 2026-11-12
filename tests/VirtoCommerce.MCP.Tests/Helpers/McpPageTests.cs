using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Common;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpPageTests
{
    [Fact]
    public void Create_TellsTheNextSkip_WhenThereIsMore()
    {
        var page = McpPage<int>.Create([1, 2, 3], totalCount: 10, new McpPaging(Skip: 3, Take: 3));

        Assert.Equal(6, page.NextSkip);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void Create_HasNoNextSkip_OnTheLastPage()
    {
        var page = McpPage<int>.Create([7, 8, 9, 10], totalCount: 10, new McpPaging(Skip: 6, Take: 5));

        Assert.Null(page.NextSkip);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Create_HasNoNextSkip_ForAnEmptyPage()
    {
        var page = McpPage<int>.Create([], totalCount: 10, new McpPaging(Skip: 20, Take: 5));

        Assert.Null(page.NextSkip);
    }

    // Two pages together hold every item once: the list_modules lesson.
    [Fact]
    public void FromList_TwoPages_HoldEveryItemOnce()
    {
        var all = Enumerable.Range(1, 91).ToList();

        var first = McpPage<int>.FromList(all, new McpPaging(0, 50));
        var second = McpPage<int>.FromList(all, new McpPaging(first.NextSkip!.Value, 50));

        Assert.Equal(91, first.TotalCount);
        Assert.Equal(50, first.Items.Count);
        Assert.Equal(41, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.Equal(all, first.Items.Concat(second.Items));
    }

    [Fact]
    public void Serialize_UsesTheNamesTheAgentSees()
    {
        var page = McpPage<string>.Create(["a"], totalCount: 2, new McpPaging(0, 1));

        var json = JsonSerializer.SerializeToElement(page, McpSerialization.Options);

        Assert.Equal(["items", "total_count", "skip", "take", "next_skip", "has_more"], json.EnumerateObject().Select(x => x.Name));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-5, 0)]
    [InlineData(40, 40)]
    public async Task GetPagingAsync_NeverSkipsBackwards(int? skip, int expected)
    {
        var resolver = Resolver(maxTake: 50);

        var paging = await resolver.GetPagingAsync(skip, take: null);

        Assert.Equal(expected, paging.Skip);
    }

    [Fact]
    public async Task GetPagingAsync_TakesThePageSizeOfThePlatform()
    {
        var resolver = Resolver(maxTake: 50);

        var paging = await resolver.GetPagingAsync(skip: 0, take: 500);

        Assert.Equal(50, paging.Take);
    }

    [Fact]
    public void WithPaging_SetsTheSearchCriteria()
    {
        var criteria = new TestCriteria().WithPaging(new McpPaging(20, 10));

        Assert.Equal(20, criteria.Skip);
        Assert.Equal(10, criteria.Take);
    }

    [Fact]
    public void ToMcpPage_MapsASearchResult()
    {
        var result = new GenericSearchResult<TestItem> { TotalCount = 7, Results = [new TestItem { Id = "1" }, new TestItem { Id = "2" }] };

        var page = result.ToMcpPage(new McpPaging(0, 2), x => x.Id);

        Assert.Equal(["1", "2"], page.Items);
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(2, page.NextSkip);
    }

    private static IMcpPageSizeResolver Resolver(int maxTake)
    {
        var resolver = new Mock<IMcpPageSizeResolver>();
        resolver.Setup(x => x.GetPageSizeAsync(It.IsAny<int?>())).ReturnsAsync((int? take) => System.Math.Clamp(take ?? 20, 1, maxTake));
        return resolver.Object;
    }

    private sealed class TestCriteria : SearchCriteriaBase;

    private sealed class TestItem : Entity;
}
