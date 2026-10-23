using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests;

public abstract class McpTestsBase : IDisposable
{
    public const string RequesterId = "requester-id";
    public const string ApproverId = "approver-id";
    public const string ToolName = "activate_promotion";

    private readonly SqliteConnection _connection;

    protected McpTestsBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.EnsureCreated();
    }

    public TestTimeProvider TimeProvider { get; } = new();

    public Mock<ISettingsManager> SettingsManager { get; } = new();

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    public McpDbContext CreateDbContext()
    {
        return new McpDbContext(new DbContextOptionsBuilder<McpDbContext>().UseSqlite(_connection).Options);
    }

    public IMcpRepository CreateRepository()
    {
        return new McpRepository(CreateDbContext());
    }

    public void SetSetting(SettingDescriptor setting, object value)
    {
        SettingsManager
            .Setup(x => x.GetObjectSettingAsync(setting.Name, null, null))
            .ReturnsAsync(new ObjectSettingEntry { Name = setting.Name, Value = value });
    }

    public async Task SeedAsync(params object[] entities)
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async Task<string[]> GetIdsAsync<TEntity>()
        where TEntity : class, IEntity
    {
        await using var dbContext = CreateDbContext();
        var ids = await dbContext.Set<TEntity>().Select(x => x.Id).ToListAsync(TestContext.Current.CancellationToken);

        return ids.Order().ToArray();
    }

    public async Task<McpPendingChangeEntity> GetPendingChangeAsync(string id)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<McpPendingChangeEntity>().AsNoTracking().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
    }

    public McpPendingChangeEntity PendingChange(string id, string status = Core.Models.McpPendingChangeStatus.Pending, DateTime? created = null, DateTime? modified = null, DateTime? expiration = null)
    {
        var createdDate = created ?? TimeProvider.UtcNow;

        return new McpPendingChangeEntity
        {
            Id = id,
            ScopeName = McpScopes.Admin,
            ToolName = ToolName,
            Arguments = "{\"promotionId\":\"p1\"}",
            Status = status,
            RequestedByUserId = RequesterId,
            RequestedByUserName = "requester",
            CreatedDate = createdDate,
            ModifiedDate = modified,
            ExpirationDate = expiration ?? createdDate.AddHours(72),
        };
    }

    public McpAuditEntryEntity AuditEntry(string id, DateTime created)
    {
        return new McpAuditEntryEntity
        {
            Id = id,
            ScopeName = McpScopes.Admin,
            ToolName = ToolName,
            Outcome = Core.Models.McpAuditOutcome.Succeeded,
            CreatedDate = created,
        };
    }
}
