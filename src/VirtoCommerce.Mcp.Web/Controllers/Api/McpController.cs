using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Permissions = VirtoCommerce.Mcp.Core.ModuleConstants.Security.Permissions;

namespace VirtoCommerce.Mcp.Web.Controllers.Api;

[Authorize]
[Route("api/mcp")]
public class McpController : Controller
{
    // GET: api/mcp
    /// <summary>
    /// Get message
    /// </summary>
    /// <remarks>Return "Hello world!" message</remarks>
    [HttpGet]
    [Route("")]
    [Authorize(Permissions.Read)]
    public ActionResult<string> Get()
    {
        return Ok(new { result = "Hello world!" });
    }
}
