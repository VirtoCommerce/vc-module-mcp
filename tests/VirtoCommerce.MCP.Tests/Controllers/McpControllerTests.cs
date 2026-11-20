using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Web.Controllers.Api;
using VirtoCommerce.MCP.Web.Services;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Controllers;

[Trait("Category", "Unit")]
public class McpControllerTests
{
    // A decision on a change is a person's: the back office's approve and reject refuse an agent's token, and the answer
    // names the screen where the user decides.
    [Fact]
    public async Task ReviewActions_Should_RefuseAnAgentsToken_AndNameAgentApprovals()
    {
        // Arrange
        var controller = CreateControllerForAnAgent();

        // Act
        var approve = await controller.ApprovePendingChange("change-1", request: null);
        var reject = await controller.RejectPendingChange("change-1", request: null);

        // Assert
        foreach (var result in new[] { approve.Result, reject.Result })
        {
            var refusal = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, refusal.StatusCode);
            Assert.Equal($"An AI agent cannot review pending changes here. The user reviews them in {McpAdminShellLinks.ApprovalsScreen}.", GetMessage(refusal));
        }
    }

    // The refusal comes before any review, so the services behind the review are not needed.
    private static McpController CreateControllerForAnAgent()
    {
        var tokenClassifier = new Mock<McpAgentTokenClassifier>(Array.Empty<McpScope>(), null, null);
        tokenClassifier.Setup(x => x.IsAgentTokenAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(true);

        return new McpController(null, null, null, null, null, null, null, tokenClassifier.Object, null, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Bearer")) } },
        };
    }

    private static string GetMessage(ObjectResult result)
    {
        return (string)result.Value!.GetType().GetProperty("message")!.GetValue(result.Value);
    }
}
