using System.Threading.Tasks;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.Platform.Core.GenericCrud;

namespace VirtoCommerce.MCP.Core.Services;

public interface IMcpJobService : ICrudService<McpJob>
{
    // Saves the job's status, engine job id, dates and message only while its stored status is still the expected one,
    // so a cancel and a start that race each other cannot both win. False when the status changed in the meantime.
    Task<bool> TryChangeStatusAsync(McpJob job, string expectedStatus);
}
