using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.SampleModule.Tools;
using VirtoCommerce.Platform.Core.Modularity;

namespace VirtoCommerce.MCP.SampleModule;

public class Module : IModule
{
    public ManifestModuleInfo ModuleInfo { get; set; }

    public void Initialize(IServiceCollection serviceCollection)
    {
        serviceCollection.AddMcpTools<UserTools>();
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        // Nothing to set up after the tools are registered.
    }

    public void Uninstall()
    {
        // Nothing to clean up.
    }
}
