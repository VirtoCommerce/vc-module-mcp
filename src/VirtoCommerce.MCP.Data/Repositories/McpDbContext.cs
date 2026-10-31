using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VirtoCommerce.MCP.Data.Models;
using VirtoCommerce.Platform.Data.Extensions;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.MCP.Data.Repositories;

public class McpDbContext : DbContextBase
{
    public McpDbContext(DbContextOptions<McpDbContext> options)
        : base(options)
    {
    }

    protected McpDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<McpPendingChangeEntity>().ToAuditableEntityTable("McpPendingChange");
        modelBuilder.Entity<McpPendingChangeEntity>().HasIndex(x => new { x.Status, x.ExpirationDate });
        modelBuilder.Entity<McpPendingChangeEntity>().HasIndex(x => x.RequestedByUserId);

        modelBuilder.Entity<McpAuditEntryEntity>().ToAuditableEntityTable("McpAuditEntry");
        modelBuilder.Entity<McpAuditEntryEntity>().HasIndex(x => x.CreatedDate);
        modelBuilder.Entity<McpAuditEntryEntity>().HasIndex(x => new { x.UserId, x.CreatedDate });
        modelBuilder.Entity<McpAuditEntryEntity>().HasIndex(x => x.PendingChangeId);

        modelBuilder.Entity<McpJobEntity>().ToAuditableEntityTable("McpJob");
        modelBuilder.Entity<McpJobEntity>().HasIndex(x => x.CreatedDate);
        modelBuilder.Entity<McpJobEntity>().HasIndex(x => x.UserId);

        switch (Database.ProviderName)
        {
            case "Pomelo.EntityFrameworkCore.MySql":
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.Load("VirtoCommerce.MCP.Data.MySql"));
                break;
            case "Npgsql.EntityFrameworkCore.PostgreSQL":
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.Load("VirtoCommerce.MCP.Data.PostgreSql"));
                break;
            case "Microsoft.EntityFrameworkCore.SqlServer":
                modelBuilder.ApplyConfigurationsFromAssembly(Assembly.Load("VirtoCommerce.MCP.Data.SqlServer"));
                break;
        }
    }
}
