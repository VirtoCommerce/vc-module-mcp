using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Web;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.Platform.Core.Jobs;
using Xunit;

namespace VirtoCommerce.MCP.Tests;

[Trait("Category", "Unit")]
public class ModuleTests
{
    [Theory]
    [InlineData(typeof(IMcpScopeRegistry), ServiceLifetime.Singleton)]
    [InlineData(typeof(McpCallToolFilter), ServiceLifetime.Scoped)]
    public void Initialize_Should_RegisterServicesWithTheExpectedLifetime(Type serviceType, ServiceLifetime lifetime)
    {
        // Act
        var services = Initialize();

        // Assert
        Assert.Equal(lifetime, services.Single(x => x.ServiceType == serviceType).Lifetime);
    }

    [Fact]
    public void Initialize_Should_RegisterTheBuiltInScopes()
    {
        // Act
        var scopes = Initialize()
            .Where(x => x.ServiceType == typeof(McpScope))
            .Select(x => (McpScope)x.ImplementationInstance)
            .ToDictionary(x => x.Name);

        // Assert
        Assert.Equal([McpScopes.Admin, McpScopes.Vendor], scopes.Keys.Order().ToArray());
        Assert.True(scopes[McpScopes.Admin].RequireAuthenticatedUser);
        Assert.Equal([ModuleConstants.Security.Permissions.Access], scopes[McpScopes.Admin].Permissions);
        Assert.Equal(["vendor_portal:access"], scopes[McpScopes.Vendor].Permissions);
    }

    [Fact]
    public void Initialize_Should_ScheduleTheCleanupJobFromSettings()
    {
        // Act
        var registration = Initialize()
            .Select(x => x.ImplementationInstance)
            .OfType<RecurringJobRegistration>()
            .Single(x => x.Id == ModuleConstants.Jobs.CleanupJobId);

        // Assert
        Assert.Equal(ModuleConstants.Settings.Cleanup.Enabled, registration.EnablerSetting);
        Assert.Equal(ModuleConstants.Settings.Cleanup.Cron, registration.CronSetting);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://admin.example.com", null)]
    [InlineData("http://localhost:10645/", null)]
    [InlineData(null, "https://store.example.com")]
    public void Initialize_Should_AcceptValidOrigins(string publicOrigin, string scopeOrigin)
    {
        // Arrange
        using var serviceProvider = Initialize(Configuration(publicOrigin, scopeOrigin)).BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<McpOptions>>().Value;

        // Assert
        Assert.Equal(publicOrigin, options.PublicOrigin);
    }

    [Theory]
    [InlineData("admin.example.com", null)]
    [InlineData("ftp://admin.example.com", null)]
    [InlineData("https://admin.example.com/admin/mcp", null)]
    [InlineData("https://admin.example.com?x=1", null)]
    [InlineData(null, "https://store.example.com/ucp")]
    public void Initialize_Should_RejectInvalidOrigins(string publicOrigin, string scopeOrigin)
    {
        // Arrange
        using var serviceProvider = Initialize(Configuration(publicOrigin, scopeOrigin)).BuildServiceProvider();

        // Act & Assert
        Assert.Throws<OptionsValidationException>(() => serviceProvider.GetRequiredService<IOptions<McpOptions>>().Value);
    }

    private static Dictionary<string, string> Configuration(string publicOrigin, string scopeOrigin)
    {
        var configuration = new Dictionary<string, string>();

        if (publicOrigin != null)
        {
            configuration["MCP:PublicOrigin"] = publicOrigin;
        }

        if (scopeOrigin != null)
        {
            configuration["MCP:Scopes:ucp:PublicOrigin"] = scopeOrigin;
        }

        return configuration;
    }

    private static ServiceCollection Initialize(Dictionary<string, string> configuration = null)
    {
        var services = new ServiceCollection();
        var module = new Module
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build(),
        };

        module.Initialize(services);

        return services;
    }
}
