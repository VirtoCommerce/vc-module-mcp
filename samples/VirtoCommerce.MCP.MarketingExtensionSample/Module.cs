using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.MarketingExtensionSample.Models;
using VirtoCommerce.MCP.MarketingExtensionSample.Services;
using VirtoCommerce.MCP.MarketingSample.Data;
using VirtoCommerce.MCP.MarketingSample.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Modularity;

namespace VirtoCommerce.MCP.MarketingExtensionSample;

// A client module that extends the marketing sample without changing it. The dependency in module.manifest makes the
// platform initialize it after the sample, so its registrations win.
public class Module : IModule, IHasConfiguration
{
    public ManifestModuleInfo ModuleInfo { get; set; }

    public IConfiguration Configuration { get; set; }

    public void Initialize(IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions<ContosoMarketingOptions>().Bind(Configuration.GetSection(ContosoMarketingOptions.SectionName));

        // 1. Extend the model: every promotion the sample creates is a ContosoPromotion.
        AbstractTypeFactory<DemoPromotion>.OverrideType<DemoPromotion, ContosoPromotion>();

        // 2. Replace a service: the sample's tools resolve MarketingAnalytics and get Contoso's rules.
        serviceCollection.AddTransient<MarketingAnalytics, ContosoMarketingAnalytics>();

        // 3. Add tools, prompts and resources to the same MCP scope.
        var assembly = typeof(Module).Assembly;
        serviceCollection.AddMcpToolsFromAssembly(assembly);
        serviceCollection.AddMcpPromptsFromAssembly(assembly);
        serviceCollection.AddMcpResourcesFromAssembly(assembly);
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        // Nothing to set up after the services are registered.
    }

    public void Uninstall()
    {
        // Nothing to clean up.
    }
}
