using System;
using System.Threading.Tasks;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Web.Services;

public class McpPageSizeResolver(
    ISettingsManager settingsManager) : IMcpPageSizeResolver
{
    private const int DefaultTake = 20;

    protected virtual int DefaultPageSize => DefaultTake;

    public virtual async Task<int> GetPageSizeAsync(int? take)
    {
        var maxTake = await settingsManager.GetValueAsync<int>(ModuleConstants.Settings.Tools.MaxTake);

        return Math.Clamp(take ?? DefaultPageSize, 1, maxTake);
    }
}
