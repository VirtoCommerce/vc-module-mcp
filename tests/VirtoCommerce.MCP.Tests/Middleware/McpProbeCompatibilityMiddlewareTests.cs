using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VirtoCommerce.MCP.Web.Middleware;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Middleware;

[Trait("Category", "Unit")]
public class McpProbeCompatibilityMiddlewareTests
{
    [Theory]
    [InlineData("event: message\ndata: {\"id\":1}\n\n", "{\"id\":1}")]
    [InlineData("data: {\"id\":1,\ndata: \"result\":{}}\n\n", "{\"id\":1,\n\"result\":{}}")]
    [InlineData(": keep-alive\n\n", ": keep-alive\n\n")]
    public async Task InvokeAsync_Should_ReturnTheEventData_When_AJsonOnlyClientListsTools(string eventStream, string expected)
    {
        // Arrange
        var middleware = new McpProbeCompatibilityMiddleware(context =>
        {
            context.Response.ContentType = "text/event-stream";
            return context.Response.WriteAsync(eventStream);
        });
        var context = CreateToolsListContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal(expected, ReadBody(context));
    }

    private static DefaultHttpContext CreateToolsListContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Accept = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }
}
