using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Extensions;

[Trait("Category", "Unit")]
public class McpCapabilitiesRegistrationTests
{
    [Fact]
    public void AddMcpCapabilities_Should_RegisterTheTools_Prompts_AndResources_OfTheMarkersAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMcpCapabilities<McpCapabilitiesRegistrationTests>();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.Contains(provider.GetServices<McpServerTool>(), x => x.ProtocolTool.Name == "capability_marker_tool");
        Assert.Contains(provider.GetServices<McpServerPrompt>(), x => x.ProtocolPrompt.Name == "capability_marker_prompt");
        Assert.Contains(provider.GetServices<McpServerResource>(), x => x.ProtocolResourceTemplate.UriTemplate == "test://capability-marker");
    }

    // One set of options for every module: snake_case, nulls left out, and nobody can change it for the others.
    [Fact]
    public void Options_Should_WriteSnakeCase_LeaveOutNulls_AndBeReadOnly()
    {
        var json = JsonSerializer.Serialize(new { StoreId = "B2B-store", already_snake = 1, Missing = (string)null }, McpSerialization.Options);

        Assert.Equal("""{"store_id":"B2B-store","already_snake":1}""", json);
        Assert.True(McpSerialization.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => McpSerialization.Options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
    }

    [McpServerToolType]
    public sealed class CapabilityMarkerTools
    {
        [McpServerTool(Name = "capability_marker_tool")]
        [Description("A tool of the test assembly.")]
        public static string Run() => "ok";
    }

    [McpServerPromptType]
    public static class CapabilityMarkerPrompts
    {
        [McpServerPrompt(Name = "capability_marker_prompt")]
        [Description("A prompt of the test assembly.")]
        public static string Prompt() => "ok";
    }

    [McpServerResourceType]
    public static class CapabilityMarkerResources
    {
        [McpServerResource(UriTemplate = "test://capability-marker", Name = "capability_marker_resource")]
        [Description("A resource of the test assembly.")]
        public static string Read() => "ok";
    }
}
