using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using VirtoCommerce.MCP.Core.Models;

namespace VirtoCommerce.MCP.Core.Services;

// Writes the HTTP responses that refuse a request before it reaches the MCP server. A module can replace the bodies
// to keep the error format its clients expect; the status codes and the WWW-Authenticate header are part of the MCP
// authorization flow and should be kept.
public class McpChallengeWriter
{
    public virtual async Task WriteChallengeAsync(HttpContext context, McpChallenge challenge)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = GetWwwAuthenticate(challenge);
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        await context.Response.WriteAsJsonAsync(GetChallengeBody(challenge), context.RequestAborted);
    }

    public virtual async Task WriteForbiddenAsync(HttpContext context, McpScope scope, string message)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        await context.Response.WriteAsJsonAsync(GetForbiddenBody(scope, message), context.RequestAborted);
    }

    protected virtual string GetWwwAuthenticate(McpChallenge challenge)
    {
        var header = $"Bearer realm=\"{Escape(challenge.Origin)}\", resource_metadata=\"{Escape(challenge.ResourceMetadataUrl)}\", " +
            $"scope=\"{string.Join(' ', ModuleConstants.Security.OAuthScopes)}\"";

        if (!string.IsNullOrEmpty(challenge.Error))
        {
            header += $", error=\"{Escape(challenge.Error)}\", error_description=\"{Escape(challenge.ErrorDescription)}\"";
        }

        return header;
    }

    protected virtual object GetChallengeBody(McpChallenge challenge)
    {
        return new { error = challenge.Error ?? "unauthorized", error_description = challenge.ErrorDescription };
    }

    protected virtual object GetForbiddenBody(McpScope scope, string message)
    {
        return new { error = "forbidden", error_description = message };
    }

    protected static string Escape(string value)
    {
        return value?.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
