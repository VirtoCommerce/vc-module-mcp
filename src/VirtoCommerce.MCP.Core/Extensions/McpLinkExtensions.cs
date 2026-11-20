namespace VirtoCommerce.MCP.Core.Extensions;

public static class McpLinkExtensions
{
    // A screen with its link in brackets, such as "Stores (https://admin.example.com/#!/workspace/store)", or the screen
    // alone when there is no link (IMcpLinkResolver).
    public static string WithLink(this string screen, string link)
    {
        return string.IsNullOrEmpty(link) ? screen : $"{screen} ({link})";
    }
}
