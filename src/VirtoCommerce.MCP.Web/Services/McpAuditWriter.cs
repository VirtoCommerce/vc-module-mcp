using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Web.Services;

public class McpAuditWriter(
    IMcpAuditEntryService auditEntryService,
    ISettingsManager settingsManager,
    ILogger<McpAuditWriter> logger)
{
    private const int MaxArgumentsLength = 32 * 1024;
    private const int MaxErrorMessageLength = 2048;

    private static readonly string[] _clientIdClaimTypes = ["client_id", "azp", "oi_prst"];

    public static string GetFailureOutcome(Exception exception)
    {
        return exception is McpException and not McpProtocolException
            ? McpAuditOutcome.Failed
            : McpAuditOutcome.Error;
    }

    public virtual McpAuditEntry Create(string scopeName, string toolName, ClaimsPrincipal user)
    {
        var entry = AbstractTypeFactory<McpAuditEntry>.TryCreateInstance();

        entry.ScopeName = scopeName;
        entry.ToolName = toolName;
        entry.UserId = user?.GetUserId();
        entry.UserName = user?.GetUserName();
        entry.ClientId = user?.FindAnyValue(_clientIdClaimTypes);

        return entry;
    }

    public virtual async Task WriteAsync(McpAuditEntry entry, IDictionary<string, JsonElement> arguments, IEnumerable<string> sensitiveArgumentPaths)
    {
        try
        {
            if (!await settingsManager.GetValueAsync<bool>(ModuleConstants.Settings.Audit.Enabled))
            {
                return;
            }

            if (arguments != null && await settingsManager.GetValueAsync<bool>(ModuleConstants.Settings.Audit.LogArguments))
            {
                var argumentsJson = JsonSerializer.Serialize(arguments, McpJsonUtilities.DefaultOptions);
                entry.Arguments = Truncate(McpSensitiveArguments.Mask(argumentsJson, sensitiveArgumentPaths), MaxArgumentsLength);
            }

            entry.ErrorMessage = Truncate(entry.ErrorMessage, MaxErrorMessageLength);

            await auditEntryService.SaveChangesAsync([entry]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write the MCP audit entry for tool {ToolName} in scope {ScopeName}", entry.ToolName, entry.ScopeName);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value?.Length > maxLength ? value[..maxLength] : value;
    }
}
