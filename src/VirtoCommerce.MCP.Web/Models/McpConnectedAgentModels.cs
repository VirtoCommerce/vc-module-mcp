using System;
using System.Collections.Generic;

namespace VirtoCommerce.MCP.Web.Models;

// An AI agent a user connected to an MCP endpoint: the user's stored OAuth authorization of an MCP client.
public class McpConnectedAgent
{
    // Id of the OAuth authorization; revoking it signs the agent out.
    public string Id { get; set; }

    public string ClientId { get; set; }

    public string ClientName { get; set; }

    public string UserId { get; set; }

    public string UserName { get; set; }

    // The MCP endpoints the client may get tokens for, for example https://admin.example.com/admin/mcp.
    public IList<string> Resources { get; set; } = [];

    public DateTime? ConnectedDate { get; set; }

    // When the agent last got a token: its sign-in or its last token refresh.
    public DateTime? LastUsedDate { get; set; }

    // Sign-ins that can still refresh their tokens.
    public int ActiveSessionCount { get; set; }
}

public class McpConnectedAgentSearchCriteria
{
    public string UserId { get; set; }

    public string ClientId { get; set; }

    public int Skip { get; set; }

    public int Take { get; set; } = 20;
}

public class McpConnectedAgentSearchResult
{
    public int TotalCount { get; set; }

    public IList<McpConnectedAgent> Results { get; set; } = [];
}

public enum McpConnectedAgentRevokeResult
{
    Revoked,
    NotFound,
    Forbidden,
}
