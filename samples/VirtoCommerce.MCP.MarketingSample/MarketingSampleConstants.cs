namespace VirtoCommerce.MCP.MarketingSample;

public static class MarketingSampleConstants
{
    // Permissions of the Marketing module. marketing:read can be limited to stores with MarketingStoreSelectedScope.
    public const string ReadPermission = "marketing:read";
    public const string CreatePermission = "marketing:create";
    public const string UpdatePermission = "marketing:update";

    // Permissions of the Content module, for the content toolset.
    public const string ContentReadPermission = "content:read";
    public const string ContentCreatePermission = "content:create";
    public const string ContentUpdatePermission = "content:update";

    public const string StoreScopeType = "MarketingStoreSelectedScope";

    public const int CouponsWithoutApproval = 1_000;

    // The discount policy of the demo company, also published as the marketing://policies/discounts resource.
    public const decimal MaxPercentWithoutSignOff = 25m;
    public const decimal MaxPercent = 50m;
    public const decimal BudgetWarningShare = 0.85m;
    public const int MinExclusivePriority = 50;

    public const int MaxCouponsPerJob = 100_000;
}
