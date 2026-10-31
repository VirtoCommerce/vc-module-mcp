using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Web.Models;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Services;

// The agents connected to MCP endpoints. An agent is an OAuth client registered for an MCP resource (rsrc:.../mcp);
// each user who signs in to it gets one stored authorization, which lives until it is revoked.
public class McpConnectedAgentService(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IUserSessionsService userSessionsService,
    Func<UserManager<ApplicationUser>> userManagerFactory)
{
    private const string McpResourceSuffix = "/mcp";

    public virtual async Task<McpConnectedAgentSearchResult> SearchAsync(McpConnectedAgentSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var agents = new List<McpConnectedAgent>();

        // Each list is read to the end before the next query: the OpenIddict stores share one database connection.
        var applications = await applicationManager.ListAsync().ToListAsync();

        foreach (var application in applications)
        {
            var resources = await GetMcpResourcesAsync(application);
            var clientId = await applicationManager.GetClientIdAsync(application);

            if (resources.Count > 0 && (string.IsNullOrEmpty(criteria.ClientId) || string.Equals(criteria.ClientId, clientId, StringComparison.Ordinal)))
            {
                agents.AddRange(await GetAgentsAsync(application, clientId, resources, criteria.UserId));
            }
        }

        var page = agents
            .OrderByDescending(x => x.LastUsedDate ?? x.ConnectedDate)
            .Skip(Math.Max(criteria.Skip, 0))
            .Take(Math.Max(criteria.Take, 0))
            .ToList();

        await SetUserNamesAsync(page);

        return new McpConnectedAgentSearchResult { TotalCount = agents.Count, Results = page };
    }

    // Signs the agent out: the authorization and its tokens are revoked, so the agent's next request is refused and
    // the user signs in again to reconnect. A user revokes their own agents; canRevokeAny allows every user's.
    public virtual async Task<McpConnectedAgentRevokeResult> RevokeAsync(ClaimsPrincipal user, string authorizationId, bool canRevokeAny)
    {
        var authorization = string.IsNullOrEmpty(authorizationId) ? null : await authorizationManager.FindByIdAsync(authorizationId);
        if (authorization == null || !await IsMcpAuthorizationAsync(authorization))
        {
            return McpConnectedAgentRevokeResult.NotFound;
        }

        var subject = await authorizationManager.GetSubjectAsync(authorization);
        if (!canRevokeAny && !string.Equals(subject, user?.GetUserId(), StringComparison.OrdinalIgnoreCase))
        {
            return McpConnectedAgentRevokeResult.Forbidden;
        }

        await userSessionsService.TerminateUserSessionGroup(authorizationId);

        return McpConnectedAgentRevokeResult.Revoked;
    }

    // An authorization of an MCP client: this API never touches the user's other sessions, such as the back office.
    protected virtual async Task<bool> IsMcpAuthorizationAsync(object authorization)
    {
        var applicationId = await authorizationManager.GetApplicationIdAsync(authorization);
        var application = string.IsNullOrEmpty(applicationId) ? null : await applicationManager.FindByIdAsync(applicationId);

        return application != null && (await GetMcpResourcesAsync(application)).Count > 0;
    }

    protected virtual async Task<IList<string>> GetMcpResourcesAsync(object application)
    {
        var permissions = await applicationManager.GetPermissionsAsync(application);

        return permissions
            .Where(x => x.StartsWith(OpenIddictConstants.Permissions.Prefixes.Resource, StringComparison.Ordinal))
            .Select(x => x[OpenIddictConstants.Permissions.Prefixes.Resource.Length..])
            .Where(x => x.TrimEnd('/').EndsWith(McpResourceSuffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<IList<McpConnectedAgent>> GetAgentsAsync(object application, string clientId, IList<string> resources, string userId)
    {
        var applicationId = await applicationManager.GetIdAsync(application);
        var clientName = await applicationManager.GetDisplayNameAsync(application) ?? clientId;
        var agents = new List<McpConnectedAgent>();
        var authorizations = await authorizationManager.FindByApplicationIdAsync(applicationId).ToListAsync();

        foreach (var authorization in authorizations)
        {
            var subject = await authorizationManager.GetSubjectAsync(authorization);

            if (await authorizationManager.HasStatusAsync(authorization, OpenIddictConstants.Statuses.Valid) &&
                (string.IsNullOrEmpty(userId) || string.Equals(subject, userId, StringComparison.OrdinalIgnoreCase)))
            {
                var agent = new McpConnectedAgent
                {
                    Id = await authorizationManager.GetIdAsync(authorization),
                    ClientId = clientId,
                    ClientName = clientName,
                    UserId = subject,
                    Resources = resources,
                    ConnectedDate = (await authorizationManager.GetCreationDateAsync(authorization))?.UtcDateTime,
                };

                await SetTokenActivityAsync(agent);
                agents.Add(agent);
            }
        }

        return agents;
    }

    private async Task SetTokenActivityAsync(McpConnectedAgent agent)
    {
        var tokens = await tokenManager.FindByAuthorizationIdAsync(agent.Id).ToListAsync();

        foreach (var token in tokens)
        {
            var created = (await tokenManager.GetCreationDateAsync(token))?.UtcDateTime;
            if (created > agent.LastUsedDate || agent.LastUsedDate == null)
            {
                agent.LastUsedDate = created;
            }

            if (await tokenManager.HasTypeAsync(token, OpenIddictConstants.TokenTypeIdentifiers.RefreshToken) &&
                await tokenManager.HasStatusAsync(token, OpenIddictConstants.Statuses.Valid))
            {
                agent.ActiveSessionCount++;
            }
        }
    }

    private async Task SetUserNamesAsync(IList<McpConnectedAgent> agents)
    {
        using var userManager = userManagerFactory();

        foreach (var group in agents.Where(x => !string.IsNullOrEmpty(x.UserId)).GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase))
        {
            var userName = (await userManager.FindByIdAsync(group.Key))?.UserName;
            foreach (var agent in group)
            {
                agent.UserName = userName;
            }
        }
    }
}
