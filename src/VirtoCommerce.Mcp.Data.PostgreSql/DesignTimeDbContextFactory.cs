using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VirtoCommerce.Mcp.Data.Repositories;

namespace VirtoCommerce.Mcp.Data.PostgreSql;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<McpDbContext>
{
    public McpDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<McpDbContext>();
        var connectionString = args.Length != 0 ? args[0] : "Server=localhost;Username=virto;Password=virto;Database=VirtoCommerce3;";

        builder.UseNpgsql(
            connectionString,
            options => options.MigrationsAssembly(typeof(PostgreSqlDataAssemblyMarker).Assembly.GetName().Name));

        return new McpDbContext(builder.Options);
    }
}
