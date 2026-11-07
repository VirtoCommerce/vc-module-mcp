using System.Collections.Generic;
using Newtonsoft.Json;

namespace VirtoCommerce.MCP.Web.Models;

public class McpProtectedResourceMetadata
{
    [JsonProperty("resource")]
    public string Resource { get; set; }

    [JsonProperty("authorization_servers")]
    public IList<string> AuthorizationServers { get; set; }

    [JsonProperty("scopes_supported")]
    public IList<string> ScopesSupported { get; set; }

    [JsonProperty("bearer_methods_supported")]
    public IList<string> BearerMethodsSupported { get; set; }

    [JsonProperty("resource_name")]
    public string ResourceName { get; set; }
}
