using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
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
using VirtoCommerce.MCP.Web.Jobs;
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
        AddDatabase(serviceCollection);
        AddOptions(serviceCollection);
        AddServices(serviceCollection);
        AddScopes(serviceCollection);

        serviceCollection.AddRecurringJob<McpCleanupJob, McpCleanupJobPayload>(schedule => schedule
            .WithId(ModuleConstants.Jobs.CleanupJobId)
            .FromSettings(ModuleConstants.Settings.Cleanup.Enabled, ModuleConstants.Settings.Cleanup.Cron)
            .WithMaxRetryAttempts(0));

        serviceCollection.AddBackgroundJob<McpTrackedJobHandler, McpTrackedJobPayload>(triggerable: false);

        serviceCollection.AddScopedMcpServer();
        serviceCollection.AddMcpTools<McpApprovalTools>();
        serviceCollection.AddMcpTools<McpJobTools>();
    }

    private void AddDatabase(IServiceCollection serviceCollection)
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
    }

    private void AddOptions(IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions<McpOptions>()
            .Bind(Configuration.GetSection(ModuleConstants.ConfigurationSection))
            .Validate(x => IsValidOrigin(x.PublicOrigin), "MCP:PublicOrigin must be an absolute http or https origin without a path, for example https://admin.example.com.")
            .Validate(x => x.Scopes.Values.All(scope => IsValidOrigin(scope.PublicOrigin)), "MCP:Scopes:{scope}:PublicOrigin must be an absolute http or https origin without a path.")
            .Validate(HasUniqueHosts, "MCP:Scopes:{scope}:Hosts: a host can serve one scope only.")
            .ValidateOnStart();
    }

    private static void AddServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddTransient<IMcpRepository, McpRepository>();
        serviceCollection.AddTransient<Func<IMcpRepository>>(provider => () => provider.CreateScope().ServiceProvider.GetRequiredService<IMcpRepository>());

        serviceCollection.AddTransient<IMcpPendingChangeService, McpPendingChangeService>();
        serviceCollection.AddTransient<IMcpPendingChangeSearchService, McpPendingChangeSearchService>();
        serviceCollection.AddTransient<IMcpAuditEntryService, McpAuditEntryService>();
        serviceCollection.AddTransient<IMcpAuditEntrySearchService, McpAuditEntrySearchService>();
        serviceCollection.AddTransient<IMcpApprovalService, McpApprovalService>();
        serviceCollection.AddTransient<IMcpCleanupService, McpCleanupService>();
        serviceCollection.AddTransient<IMcpJobService, McpJobService>();
        serviceCollection.AddTransient<IMcpJobDispatcher, McpJobDispatcher>();

        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<IMcpScopeRegistry, McpScopeRegistry>();
        serviceCollection.AddTransient<IMcpPublicOriginResolver, McpPublicOriginResolver>();
        serviceCollection.AddTransient<IMcpPageSizeResolver, McpPageSizeResolver>();
        serviceCollection.AddTransient<IMcpTokenValidator, McpTokenValidator>();
        serviceCollection.AddTransient<McpChallengeWriter>();
        serviceCollection.AddTransient<IMcpSessionService, McpSessionService>();
        serviceCollection.AddTransient<McpToolAuthorizationService>();
        serviceCollection.AddTransient<McpAuditWriter>();
        serviceCollection.AddTransient<IMcpToolAvailabilityService, McpToolAvailabilityService>();
        serviceCollection.AddTransient<McpPendingChangeExecutor>();
        serviceCollection.AddTransient<McpUserPrincipalFactory>();
        serviceCollection.AddTransient<McpPendingChangeReviewService>();
        serviceCollection.AddTransient<McpConnectedAgentService>();
        serviceCollection.AddTransient<McpAgentTokenClassifier>();
        serviceCollection.AddScoped<McpCallToolFilter>();
        serviceCollection.AddHttpContextAccessor();
        serviceCollection.AddMemoryCache();
    }

    private static void AddScopes(IServiceCollection serviceCollection)
    {
        serviceCollection.AddMcpScope(new McpScope
        {
            Name = McpScopes.Admin,
            Title = "Virto Commerce Back Office",
            Instructions = "Tools for Virto Commerce back-office operators. Every call acts as the signed-in platform user and " +
                "is limited by that user's permissions. Some tools do not change data right away: they submit a change for approval " +
                "and return its id. Such a change is applied only after another user with the mcp:approve permission approves it.",
            RequireAuthenticatedUser = true,
            ApprovalPermission = ModuleConstants.Security.Permissions.Approve,
        });

        serviceCollection.AddMcpScope(new McpScope
        {
            Name = McpScopes.Vendor,
            Title = "Virto Commerce Vendor Portal",
            Instructions = "Tools for vendor portal users. Every call acts as the signed-in user and sees only their own seller's data.",
            RequireAuthenticatedUser = true,
            Permissions = [VendorPortalAccessPermission],
        });
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        var serviceProvider = appBuilder.ApplicationServices;

        var settingsRegistrar = serviceProvider.GetRequiredService<ISettingsRegistrar>();
        settingsRegistrar.RegisterSettings(ModuleConstants.Settings.AllSettings, ModuleInfo.Id);

        var permissionsRegistrar = serviceProvider.GetRequiredService<IPermissionsRegistrar>();
        permissionsRegistrar.RegisterPermissions(ModuleInfo.Id, "MCP", ModuleConstants.Security.Permissions.AllPermissions);

        ValidateMcpPrimitives(serviceProvider);
        LogScopesWithoutPublicOrigin(serviceProvider);

        // The MCP endpoints join the platform's endpoints, after its authentication and authorization (see McpStartup).
        appBuilder.UseEndpoints(endpoints => endpoints.MapMcpScopes());

        using var serviceScope = serviceProvider.CreateScope();
        using var dbContext = serviceScope.ServiceProvider.GetRequiredService<McpDbContext>();
        dbContext.Database.Migrate();
    }

    // Outside Development, a scope without a configured origin refuses bearer tokens at /{scope}/mcp: the audience
    // a token must have would otherwise come from the request's Host header.
    private static void LogScopesWithoutPublicOrigin(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        var options = serviceProvider.GetRequiredService<IOptions<McpOptions>>().Value;
        if (!string.IsNullOrEmpty(options.PublicOrigin))
        {
            return;
        }

        var logger = serviceProvider.GetRequiredService<ILogger<Module>>();
        foreach (var scope in serviceProvider.GetRequiredService<IMcpScopeRegistry>().Scopes)
        {
            options.Scopes.TryGetValue(scope.Name, out var scopeOptions);
            if (scopeOptions?.Enabled != false && string.IsNullOrEmpty(scopeOptions?.PublicOrigin))
            {
                logger.LogError("MCP scope '{ScopeName}' has no public origin: neither MCP:PublicOrigin nor MCP:Scopes:{ScopeName}:PublicOrigin is set. " +
                    "Its endpoint {Path} refuses access tokens until one is set.", scope.Name, scope.Name, scope.Path);
            }
        }
    }

    private static void ValidateMcpPrimitives(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Module>>();

        LogDuplicates(logger, "tool", serviceProvider.GetRequiredService<McpToolCatalog>().FindUnresolvedDuplicates());
        LogDuplicates(logger, "prompt", McpScopeSessionConfigurator.FindDuplicateNames(serviceProvider.GetServices<McpServerPrompt>()));
        LogDuplicates(logger, "resource", McpScopeSessionConfigurator.FindDuplicateNames(serviceProvider.GetServices<McpServerResource>()));

        foreach (var tool in serviceProvider.GetServices<McpServerTool>().Where(x =>
            !string.IsNullOrEmpty(x.Metadata.OfType<McpRequiresApprovalAttribute>().FirstOrDefault()?.Condition) && x.FindApprovalCondition() == null))
        {
            logger.LogError("MCP tool '{Name}' has an approval condition that is not a public static bool method of its class taking IReadOnlyDictionary<string, JsonElement>. Every call of the tool needs approval.",
                tool.ProtocolTool.Name);
        }

        var scopeRegistry = serviceProvider.GetRequiredService<IMcpScopeRegistry>();
        foreach (var (scopeName, name) in McpScopeSessionConfigurator.FindToolsWithoutDeclaredAccess(serviceProvider.GetServices<McpServerTool>(), scopeRegistry.Scopes))
        {
            logger.LogWarning("MCP tool '{Name}' declares no permission, so scope '{ScopeName}', which requires a signed-in user, does not serve it. " +
                "Mark it with [Authorize], [McpPermission], or [McpAnyUser] when every signed-in user may call it.",
                name, scopeName);
        }

        foreach (var name in McpServerPrimitiveExtensions.FindApprovalToolsWithoutDryRun(serviceProvider.GetServices<McpServerTool>()))
        {
            logger.LogError("MCP tool '{Name}' requires approval but has no dryRun parameter, so its change cannot be previewed. Its calls are refused.",
                name);
        }

        foreach (var (scopeName, name) in McpCallToolFilter.FindToolsThatCannotBeApproved(serviceProvider.GetServices<McpServerTool>(), scopeRegistry))
        {
            if (scopeName != null)
            {
                logger.LogError("MCP tool '{Name}' requires approval, which scope '{ScopeName}' does not have (McpScope.ApprovalPermission). Its calls in that scope are refused.",
                    name, scopeName);
            }
            else
            {
                logger.LogError("MCP tool '{Name}' requires approval but is not marked with a scope, so its calls are not held for approval. Mark it with [McpScope(McpScopes.Admin)].",
                    name);
            }
        }
    }

    private static void LogDuplicates(ILogger logger, string kind, IEnumerable<(string ScopeName, string Name)> duplicates)
    {
        foreach (var (scopeName, name) in duplicates)
        {
            logger.LogError("MCP {Kind} '{Name}' is registered more than once for {ScopeName}. Only the first registration is served.",
                kind, name, scopeName != null ? $"scope '{scopeName}'" : "endpoints without a scope");
        }
    }

    private static bool HasUniqueHosts(McpOptions options)
    {
        var hosts = options.Scopes.Values
            .Where(x => x.Enabled && x.Hosts != null)
            .SelectMany(x => x.Hosts)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => HostString.FromUriComponent(x.Trim()))
            .ToList();

        return !hosts.Where((host, index) => hosts.Skip(index + 1).Any(other =>
            string.Equals(host.Host, other.Host, StringComparison.OrdinalIgnoreCase) &&
            (host.Port == null || other.Port == null || host.Port == other.Port))).Any();
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
