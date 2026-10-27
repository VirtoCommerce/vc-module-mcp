using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.BackgroundJobs;
using VirtoCommerce.MCP.Data.MySql;
using VirtoCommerce.MCP.Data.PostgreSql;
using VirtoCommerce.MCP.Data.Repositories;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.MCP.Data.SqlServer;
using VirtoCommerce.MCP.Web.Extensions;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.MCP.Web.Tools;
using VirtoCommerce.Platform.Core.Jobs;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.Platform.Core.Settings;
using VirtoCommerce.Platform.Data.MySql.Extensions;
using VirtoCommerce.Platform.Data.PostgreSql.Extensions;
using VirtoCommerce.Platform.Data.SqlServer.Extensions;

namespace VirtoCommerce.MCP.Web;

public class Module : IModule, IHasConfiguration
{
    private const string VendorPortalAccessPermission = "vendor_portal:access";

    public ManifestModuleInfo ModuleInfo { get; set; }
    public IConfiguration Configuration { get; set; }

    public void Initialize(IServiceCollection serviceCollection)
    {
        serviceCollection.AddDbContext<McpDbContext>(options =>
        {
            var databaseProvider = Configuration.GetValue("DatabaseProvider", "SqlServer");
            var connectionString = Configuration.GetConnectionString(ModuleInfo.Id) ?? Configuration.GetConnectionString("VirtoCommerce");

            switch (databaseProvider)
            {
                case "MySql":
                    options.UseMySqlDatabase(connectionString, typeof(MySqlDataAssemblyMarker), Configuration);
                    break;
                case "PostgreSql":
                    options.UsePostgreSqlDatabase(connectionString, typeof(PostgreSqlDataAssemblyMarker), Configuration);
                    break;
                default:
                    options.UseSqlServerDatabase(connectionString, typeof(SqlServerDataAssemblyMarker), Configuration);
                    break;
            }
        });

        serviceCollection.AddOptions<McpOptions>()
            .Bind(Configuration.GetSection(ModuleConstants.ConfigurationSection))
            .Validate(x => IsValidOrigin(x.PublicOrigin), "MCP:PublicOrigin must be an absolute http or https origin without a path, for example https://admin.example.com.")
            .Validate(x => x.Scopes.Values.All(scope => IsValidOrigin(scope.PublicOrigin)), "MCP:Scopes:{scope}:PublicOrigin must be an absolute http or https origin without a path.")
            .ValidateOnStart();

        serviceCollection.AddTransient<IMcpRepository, McpRepository>();
        serviceCollection.AddTransient<Func<IMcpRepository>>(provider => () => provider.CreateScope().ServiceProvider.GetRequiredService<IMcpRepository>());

        serviceCollection.AddTransient<IMcpPendingChangeService, McpPendingChangeService>();
        serviceCollection.AddTransient<IMcpPendingChangeSearchService, McpPendingChangeSearchService>();
        serviceCollection.AddTransient<IMcpAuditEntryService, McpAuditEntryService>();
        serviceCollection.AddTransient<IMcpAuditEntrySearchService, McpAuditEntrySearchService>();
        serviceCollection.AddTransient<IMcpApprovalService, McpApprovalService>();
        serviceCollection.AddTransient<IMcpCleanupService, McpCleanupService>();

        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<IMcpScopeRegistry, McpScopeRegistry>();
        serviceCollection.AddTransient<IMcpPublicOriginResolver, McpPublicOriginResolver>();
        serviceCollection.AddTransient<IMcpPageSizeResolver, McpPageSizeResolver>();
        serviceCollection.AddTransient<McpTokenValidator>();
        serviceCollection.AddTransient<McpToolAuthorizationService>();
        serviceCollection.AddTransient<McpAuditWriter>();
        serviceCollection.AddTransient<McpToolAvailabilityService>();
        serviceCollection.AddTransient<McpPendingChangeExecutor>();
        serviceCollection.AddScoped<McpCallToolFilter>();
        serviceCollection.AddHttpContextAccessor();

        serviceCollection.AddMcpScope(new McpScope
        {
            Name = McpScopes.Admin,
            Title = "Virto Commerce Back Office",
            Instructions = "Tools for Virto Commerce back-office operators. Every call acts as the signed-in platform user and " +
                "is limited by that user's permissions. Some tools do not change data right away: they submit a change for approval " +
                "and return its id. Such a change is applied only after another user with the mcp:approve permission approves it.",
            RequireAuthenticatedUser = true,
            Permissions = [ModuleConstants.Security.Permissions.Access],
        });

        serviceCollection.AddMcpScope(new McpScope
        {
            Name = McpScopes.Vendor,
            Title = "Virto Commerce Vendor Portal",
            Instructions = "Tools for vendor portal users. Every call acts as the signed-in user and sees only their own seller's data.",
            RequireAuthenticatedUser = true,
            Permissions = [VendorPortalAccessPermission],
        });

        serviceCollection.AddRecurringJob<McpCleanupJob, McpCleanupJobPayload>(schedule => schedule
            .WithId(ModuleConstants.Jobs.CleanupJobId)
            .FromSettings(ModuleConstants.Settings.Cleanup.Enabled, ModuleConstants.Settings.Cleanup.Cron)
            .WithMaxRetryAttempts(0));

        serviceCollection.AddScopedMcpServer();
        serviceCollection.AddMcpTools<McpApprovalTools>();
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        var serviceProvider = appBuilder.ApplicationServices;

        var settingsRegistrar = serviceProvider.GetRequiredService<ISettingsRegistrar>();
        settingsRegistrar.RegisterSettings(ModuleConstants.Settings.AllSettings, ModuleInfo.Id);

        var permissionsRegistrar = serviceProvider.GetRequiredService<IPermissionsRegistrar>();
        permissionsRegistrar.RegisterPermissions(ModuleInfo.Id, "MCP", ModuleConstants.Security.Permissions.AllPermissions);

        ValidateMcpPrimitives(serviceProvider);

        using var serviceScope = serviceProvider.CreateScope();
        using var dbContext = serviceScope.ServiceProvider.GetRequiredService<McpDbContext>();
        dbContext.Database.Migrate();
    }

    private static void ValidateMcpPrimitives(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Module>>();

        LogDuplicates(logger, "tool", serviceProvider.GetServices<McpServerTool>());
        LogDuplicates(logger, "prompt", serviceProvider.GetServices<McpServerPrompt>());
        LogDuplicates(logger, "resource", serviceProvider.GetServices<McpServerResource>());

        foreach (var (scopeName, name) in McpCallToolFilter.FindToolsThatCannotBeApproved(serviceProvider.GetServices<McpServerTool>()))
        {
            if (scopeName != null)
            {
                logger.LogError("MCP tool '{Name}' requires approval, which is available in the '{AdminScope}' scope only. Its calls in scope '{ScopeName}' are refused.",
                    name, McpScopes.Admin, scopeName);
            }
            else
            {
                logger.LogError("MCP tool '{Name}' requires approval but is not marked with a scope, so its calls are not held for approval. Mark it with [McpScope(McpScopes.Admin)].",
                    name);
            }
        }
    }

    private static void LogDuplicates(ILogger logger, string kind, IEnumerable<IMcpServerPrimitive> primitives)
    {
        foreach (var (scopeName, name) in McpScopeSessionConfigurator.FindDuplicateNames(primitives))
        {
            logger.LogError("MCP {Kind} '{Name}' is registered more than once for {ScopeName}. Only the first registration is served.",
                kind, name, scopeName != null ? $"scope '{scopeName}'" : "endpoints without a scope");
        }
    }

    private static bool IsValidOrigin(string origin)
    {
        return string.IsNullOrEmpty(origin) ||
            (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsHttpScheme(uri) && IsBareOrigin(uri));
    }

    private static bool IsHttpScheme(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool IsBareOrigin(Uri uri)
    {
        return uri.PathAndQuery == "/" && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);
    }

    public void Uninstall()
    {
    }
}
