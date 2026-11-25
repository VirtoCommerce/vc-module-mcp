using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.MCP.Core.Models;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Helpers;

[Trait("Category", "Unit")]
public class McpSetupPartPlanTests
{
    [Fact]
    public void AddChange_RecordsTheFieldWithBothValues()
    {
        var plan = new McpSetupPartPlan();

        var returned = plan.AddChange("basics.name", "Old name", "New name").AddChange("basics.url", null, "https://shop.example.com");

        Assert.Same(plan, returned);
        Assert.Collection(plan.Changes,
            x =>
            {
                Assert.Equal("basics.name", x.Field);
                Assert.Equal("Old name", x.From);
                Assert.Equal("New name", x.To);
            },
            x =>
            {
                Assert.Equal("basics.url", x.Field);
                Assert.Null(x.From);
                Assert.Equal("https://shop.example.com", x.To);
            });
    }

    // Two equal values are no change, as in McpChangePreview; a new list is a change whatever its items.
    [Fact]
    public void AddChange_SkipsTwoEqualValues_ButRecordsANewList()
    {
        var plan = new McpSetupPartPlan();

        plan.AddChange("methods.cash.active", true, true)
            .AddChange("methods.cash.priority", 10m, 10.0m)
            .AddChange("basics.url", null, null)
            .AddChange("basics.currencies", new List<string> { "EUR" }, new List<string> { "EUR", "USD" });

        Assert.Equal(["basics.currencies"], plan.Changes.Select(x => x.Field));
    }

    // A plan that names no permission leaves the part's Create or Update permission to apply.
    [Fact]
    public void RequiredPermissions_AreNull_UntilThePlanNamesOne()
    {
        var plan = new McpSetupPartPlan();

        plan.RequirePermission(null).RequirePermission(string.Empty);

        Assert.Null(plan.RequiredPermissions);
    }

    [Fact]
    public void RequirePermission_AddsEachPermissionOnce_WhateverItsCase()
    {
        var plan = new McpSetupPartPlan();

        var returned = plan.RequirePermission("catalog:read").RequirePermission("catalog:update").RequirePermission("Catalog:Read");

        Assert.Same(plan, returned);
        Assert.Equal(["catalog:read", "catalog:update"], plan.RequiredPermissions);
    }
}
