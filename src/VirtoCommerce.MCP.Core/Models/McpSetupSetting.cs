namespace VirtoCommerce.MCP.Core.Models;

// A setting of a setup, with the value of the object being set up and whether a setup can change it.
public class McpSetupSetting : McpSetting
{
    // Whether a setup can set it; the others are changed in the back office only.
    public bool IsChangeable { get; set; }
}
