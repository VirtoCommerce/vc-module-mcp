namespace VirtoCommerce.MCP.Core.Models;

public class McpChallenge
{
    public McpScope Scope { get; set; }

    // Public origin of the scope. Followed by EndpointPath, it is the OAuth resource.
    public string Origin { get; set; }

    public string EndpointPath { get; set; }

    // OAuth error code, such as invalid_token. Empty when the request has no token.
    public string Error { get; set; }

    public string ErrorDescription { get; set; }

    // Name of the tool that needs a user, when the scope itself allows anonymous users.
    public string ToolName { get; set; }

    public string Resource => Origin + EndpointPath;

    public string ResourceMetadataUrl => Origin + McpScope.GetProtectedResourceMetadataPath(EndpointPath);
}
