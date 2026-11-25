using VirtoCommerce.MCP.Core.Extensions;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpLinkExtensionsTests
{
    [Theory]
    [InlineData("https://admin.example.com/#!/workspace/store", "Stores (https://admin.example.com/#!/workspace/store)")]
    [InlineData(null, "Stores")]
    [InlineData("", "Stores")]
    public void WithLink_PutsTheLinkInBrackets_WhenThereIsOne(string link, string expected)
    {
        Assert.Equal(expected, "Stores".WithLink(link));
    }
}
