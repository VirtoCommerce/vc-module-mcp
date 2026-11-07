using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.MCP.Web.Services;

// The principal of a platform user other than the one making the request, with the roles and permissions a sign-in
// gives them: the requester of a pending change, whose access is checked again when the change is approved.
public class McpUserPrincipalFactory(
    Func<UserManager<ApplicationUser>> userManagerFactory,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsPrincipalFactory)
{
    // Null when the user does not exist or is locked out.
    public virtual async Task<ClaimsPrincipal> CreateAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        using var userManager = userManagerFactory();
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        return await claimsPrincipalFactory.CreateAsync(user);
    }
}
