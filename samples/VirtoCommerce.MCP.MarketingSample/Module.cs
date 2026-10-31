using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Jobs;
using VirtoCommerce.MCP.MarketingSample.Services;
using VirtoCommerce.Platform.Core.Jobs;
using VirtoCommerce.Platform.Core.Modularity;

namespace VirtoCommerce.MCP.MarketingSample;

public class Module : IModule
{
    public ManifestModuleInfo ModuleInfo { get; set; }

    public void Initialize(IServiceCollection serviceCollection)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<MarketingDemoStore>();
        serviceCollection.AddTransient<MarketingAccess>();
        serviceCollection.AddTransient<MarketingAnalytics>();
        serviceCollection.AddTransient<PromotionEvaluator>();
        serviceCollection.AddSingleton<ContentDemoStore>();

        serviceCollection.AddBackgroundJob<GenerateCouponsJob, GenerateCouponsPayload>(triggerable: false);
        serviceCollection.AddBackgroundJob<DeactivateStaleCouponsJob, DeactivateStaleCouponsPayload>(triggerable: false);
        serviceCollection.AddBackgroundJob<CloneContentCampaignJob, CloneContentCampaignPayload>(triggerable: false);

        // One line per primitive kind registers every tool, prompt and resource class of the assembly.
        var assembly = typeof(Module).Assembly;
        serviceCollection.AddMcpToolsFromAssembly(assembly);
        serviceCollection.AddMcpPromptsFromAssembly(assembly);
        serviceCollection.AddMcpResourcesFromAssembly(assembly);
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        // Nothing to set up after the tools are registered.
    }

    public void Uninstall()
    {
        // Nothing to clean up: the demo data lives in memory.
    }
}
