using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Middleware;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Middleware;

// A body without a length (chunked) is cut off by the server past the limit, and answered with 413, not as an error.
[Trait("Category", "Unit")]
public class McpRequestSizeLimitMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_Should_AnswerTooLarge_When_AChunkedBodyPassesTheLimit()
    {
        // Arrange: the server throws when a reader goes past the limit
        var middleware = new McpRequestSizeLimitMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            Options.Create(new McpOptions { MaxRequestBodySize = 1024 }));
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Should_LetOtherBadRequestsThrough()
    {
        // Arrange
        var middleware = new McpRequestSizeLimitMiddleware(
            _ => throw new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest),
            Options.Create(new McpOptions { MaxRequestBodySize = 1024 }));

        // Act & Assert
        await Assert.ThrowsAsync<BadHttpRequestException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
    }
}
