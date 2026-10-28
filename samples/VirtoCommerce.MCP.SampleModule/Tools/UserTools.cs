using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.Platform.Core.Security.Search;
using static VirtoCommerce.Platform.Core.PlatformConstants.Security.Permissions;

namespace VirtoCommerce.MCP.SampleModule.Tools;

// Changes only users whose name starts with "demo-", so the sample cannot lock or change a real account.
[McpServerToolType]
[McpScope(McpScopes.Admin)]
public class UserTools(
    UserManager<ApplicationUser> userManager,
    IUserSearchService userSearchService)
{
    private const string DemoUserPrefix = "demo-";

    [McpServerTool(Name = "search_users", ReadOnly = true, Destructive = false, Idempotent = true)]
    [Authorize(SecurityQuery)]
    [Description("Search back-office users by name or email. Returns compact rows: user name, email, whether the user is locked.")]
    public async Task<object> SearchUsers(
        IMcpPageSizeResolver pageSizeResolver,
        [Description("Part of the user name or email.")] string keyword = null,
        [Description("Number of rows to return. Omit it for the default page size.")] int? take = null)
    {
        var result = await userSearchService.SearchUsersAsync(new UserSearchCriteria
        {
            Keyword = keyword,
            Take = await pageSizeResolver.GetPageSizeAsync(take),
        });

        return new
        {
            total_count = result.TotalCount,
            users = result.Results.Select(x => new
            {
                user_name = x.UserName,
                email = x.Email,
                locked = IsLocked(x),
            }),
        };
    }

    [McpServerTool(Name = "lock_user", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Authorize(SecurityUpdate)]
    [McpRequiresApproval]
    [Description("Lock a back-office user so that they cannot sign in. The change is submitted for approval; " +
        "tell the user the change id and that another person must approve it.")]
    public async Task<object> LockUser(
        [Description("User name.")] string user_name,
        bool dryRun = false)
    {
        var user = await GetDemoUserAsync(user_name);

        if (!dryRun)
        {
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }

        return new { user_name = user.UserName, was_locked = IsLocked(user), action = dryRun ? "will be locked" : "locked" };
    }

    [McpServerTool(Name = "unlock_user", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Authorize(SecurityUpdate)]
    [McpRequiresApproval]
    [Description("Unlock a locked back-office user. The change is submitted for approval.")]
    public async Task<object> UnlockUser(
        [Description("User name.")] string user_name,
        bool dryRun = false)
    {
        var user = await GetDemoUserAsync(user_name);

        if (!dryRun)
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        return new { user_name = user.UserName, was_locked = IsLocked(user), action = dryRun ? "will be unlocked" : "unlocked" };
    }

    [McpServerTool(Name = "update_user_email", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Authorize(SecurityUpdate)]
    [Description("Change the email of a back-office user.")]
    public async Task<object> UpdateUserEmail(
        [Description("User name.")] string user_name,
        [Description("New email.")][McpSensitive] string email)
    {
        var user = await GetDemoUserAsync(user_name);
        var result = await userManager.SetEmailAsync(user, email);

        if (!result.Succeeded)
        {
            throw new McpException(string.Join(" ", result.Errors.Select(x => x.Description)));
        }

        return new { user_name = user.UserName, email_changed = true };
    }

    private async Task<ApplicationUser> GetDemoUserAsync(string userName)
    {
        if (userName?.StartsWith(DemoUserPrefix, StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new McpException($"The sample tools change only users whose name starts with '{DemoUserPrefix}'.");
        }

        return await userManager.FindByNameAsync(userName) ?? throw new McpException($"User '{userName}' was not found.");
    }

    private static bool IsLocked(ApplicationUser user)
    {
        return user.LockoutEnd > DateTimeOffset.UtcNow;
    }
}
