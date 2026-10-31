using System.Threading.Tasks;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpPageSizeResolver
{
    Task<int> GetPageSizeAsync(int? take);
}
