using Xunit;

namespace VirtoCommerce.MCP.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class PlatformCacheCollection
{
    public const string Name = "Platform cache";
}
