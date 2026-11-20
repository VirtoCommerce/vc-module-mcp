using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Services;

[Trait("Category", "Unit")]
public class McpToolResultJsonTests
{
    // The back office shows the preview and the result as text: an approver reads the characters as they are.
    [Fact]
    public void ToIndentedString_Should_WriteCharactersAsTheyAre()
    {
        // Arrange: a preview stored with escapes, as the SDK's default options write it
        var preview = McpToolResultJson.Parse("""{"object_name":"Müller & Söhne > Köln"}""");

        // Act
        var text = McpToolResultJson.ToIndentedString(preview);

        // Assert
        Assert.Contains("\"object_name\": \"Müller & Söhne > Köln\"", text);
    }
}
