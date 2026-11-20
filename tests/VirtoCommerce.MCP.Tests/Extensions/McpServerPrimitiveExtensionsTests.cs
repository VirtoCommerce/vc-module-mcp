using System.Text.Json;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core.Extensions;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Extensions;

[Trait("Category", "Unit")]
public class McpServerPrimitiveExtensionsTests
{
    [Fact]
    public void GetArgumentNames_AreTheParametersOfTheTool_InTheirOrder()
    {
        var tool = McpServerTool.Create((string store_id, bool dryRun) => store_id, new McpServerToolCreateOptions { Name = "a_tool" });

        Assert.Equal(["store_id", "dryRun"], tool.GetArgumentNames());
    }

    // A schema that takes other names too does not limit the arguments, so none is unknown.
    [Theory]
    [InlineData("""{"type":"object","properties":{"store_id":{"type":"string"}},"additionalProperties":true}""")]
    [InlineData("""{"type":"object","properties":{"store_id":{"type":"string"}},"additionalProperties":{"type":"string"}}""")]
    [InlineData("""{"type":"object"}""")]
    public void GetArgumentNames_AreNull_WhenTheSchemaTakesOtherNames(string schema)
    {
        var tool = McpServerTool.Create((string store_id) => store_id, new McpServerToolCreateOptions { Name = "a_tool" });
        tool.ProtocolTool.InputSchema = JsonSerializer.Deserialize<JsonElement>(schema);

        Assert.Null(tool.GetArgumentNames());
    }

    [Fact]
    public void GetArgumentNames_AreTheProperties_WhenTheSchemaSaysNoOthers()
    {
        var tool = McpServerTool.Create((string store_id) => store_id, new McpServerToolCreateOptions { Name = "a_tool" });
        tool.ProtocolTool.InputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"store_id":{"type":"string"}},"additionalProperties":false}""");

        Assert.Equal(["store_id"], tool.GetArgumentNames());
    }
}
