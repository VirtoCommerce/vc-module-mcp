using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Testing;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Testing;

[Trait("Category", "Unit")]
public sealed class McpToolHarnessTests : IAsyncDisposable
{
    private readonly FixtureStore _store = new();
    private readonly McpToolHarness _harness;

    private static readonly McpTestUser _reader = McpTestUser.Create("reader").WithPermissions(FixtureTools.ReadPermission);
    private static readonly McpTestUser _writer = McpTestUser.Create("writer").WithPermissions(FixtureTools.ReadPermission, FixtureTools.UpdatePermission);
    private static readonly McpTestUser _fashionReader = McpTestUser.Create("fashion").WithScopedPermission(FixtureTools.ReadPermission, FixtureTools.StoreScopeType, "fashion");

    public McpToolHarnessTests()
    {
        var pageSize = new Mock<IMcpPageSizeResolver>();
        pageSize.Setup(x => x.GetPageSizeAsync(It.IsAny<int?>())).ReturnsAsync((int? take) => Math.Clamp(take ?? 20, 1, 10));

        _harness = McpToolHarness.Create(
            services => services.AddMcpTools<FixtureTools>(),
            services => services.AddSingleton(_store).AddSingleton(pageSize.Object),
            scope: null);
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public void GetVisibleToolNames_FollowsThePermissions()
    {
        Assert.Equal(["check_health", "get_store_report", "search_items"], _harness.GetVisibleToolNames(_reader));
        Assert.Equal(["check_health", "delete_item", "generate_codes", "get_store_report", "pause_item", "search_items"], _harness.GetVisibleToolNames(_writer));
    }

    // [Authorize] needs the permission granted globally; [McpPermission] accepts a scoped grant.
    [Fact]
    public void GetVisibleToolNames_AScopedGrant_OpensMcpPermissionToolsOnly()
    {
        Assert.Equal(["check_health", "get_store_report"], _harness.GetVisibleToolNames(_fashionReader));
    }

    [Fact]
    public void GetVisibleToolNames_AnAnonymousUser_SeesNothingInAScopeThatRequiresSignIn()
    {
        Assert.Empty(_harness.GetVisibleToolNames(McpTestUser.Anonymous()));
    }

    [Fact]
    public void GetVisibleToolNames_AnAdministrator_SeesEverything()
    {
        Assert.Equal(6, _harness.GetVisibleToolNames(McpTestUser.Administrator()).Count);
    }

    [Fact]
    public void GetVisibleToolNames_HidesASwitchedOffTool()
    {
        _harness.DisabledTools.Add("search_items");

        Assert.DoesNotContain("search_items", _harness.GetVisibleToolNames(_reader));
    }

    [Fact]
    public async Task CallAsync_ReturnsTheAnswerAsTheAgentSeesIt()
    {
        var result = await _harness.CallAsync(_reader, "search_items", new { skip = 20, take = 50 }, cancellationToken: TestContext.Current.CancellationToken);

        result.EnsureSucceeded();
        var page = result.Read<McpPage<string>>();
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(["item-21", "item-22", "item-23", "item-24", "item-25"], page.Items);
        Assert.Equal(25, result.Json.GetProperty("total_count").GetInt32());
    }

    [Fact]
    public async Task CallAsync_AToolThatNeedsApproval_RunsAsADryRun()
    {
        var result = await _harness.CallAsync(_writer, "delete_item", new { item_id = "item-1" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(McpToolCallOutcome.PendingApproval, result.Outcome);
        Assert.True(result.WasDryRun);
        Assert.True(result.Read<McpChangePreview>().DryRun);
        Assert.Empty(_store.Deleted);
    }

    [Fact]
    public async Task CallAsync_AsAnApprovedChange_Runs()
    {
        var result = await _harness.CallAsync(_writer, "delete_item", new { item_id = "item-1" }, new McpToolCallOptions { AsApprovedChange = true }, TestContext.Current.CancellationToken);

        result.EnsureSucceeded();
        Assert.Equal(["item-1"], _store.Deleted);
    }

    [Theory]
    [InlineData(5, McpToolCallOutcome.Succeeded, 5)]
    [InlineData(50, McpToolCallOutcome.PendingApproval, 0)]
    public async Task CallAsync_AnApprovalCondition_DecidesFromTheArguments(int count, McpToolCallOutcome expected, int generated)
    {
        var result = await _harness.CallAsync(_writer, "generate_codes", new { count }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(generated, _store.GeneratedCodes);
    }

    [Fact]
    public async Task CallAsync_Confirms_ByDefault()
    {
        var result = await _harness.CallAsync(_writer, "pause_item", new { item_id = "item-2" }, cancellationToken: TestContext.Current.CancellationToken);

        result.EnsureSucceeded();
        Assert.True(result.AskedForConfirmation);
        Assert.Equal(["item-2"], _store.Paused);
    }

    [Fact]
    public async Task CallAsync_ADeclinedConfirmation_DoesNotRun()
    {
        var result = await _harness.CallAsync(_writer, "pause_item", new { item_id = "item-2" }, new McpToolCallOptions { DeclineConfirmation = true }, TestContext.Current.CancellationToken);

        Assert.Equal(McpToolCallOutcome.Declined, result.Outcome);
        Assert.Empty(_store.Paused);
    }

    [Fact]
    public async Task CallAsync_ADryRun_DoesNotAsk()
    {
        var result = await _harness.CallAsync(_writer, "pause_item", new { item_id = "item-2", dryRun = true }, cancellationToken: TestContext.Current.CancellationToken);

        result.EnsureSucceeded();
        Assert.False(result.AskedForConfirmation);
        Assert.True(result.WasDryRun);
        Assert.Empty(_store.Paused);
    }

    [Fact]
    public async Task CallAsync_AStoreTheUserDoesNotManage_IsForbidden()
    {
        var allowed = await _harness.CallAsync(_fashionReader, "get_store_report", new { store_id = "fashion" }, cancellationToken: TestContext.Current.CancellationToken);
        var refused = await _harness.CallAsync(_fashionReader, "get_store_report", new { store_id = "electronics" }, cancellationToken: TestContext.Current.CancellationToken);

        allowed.EnsureSucceeded();
        Assert.Equal(McpToolCallOutcome.Forbidden, refused.Outcome);
        Assert.Equal("You do not manage store 'electronics'.", refused.Error);
    }

    [Fact]
    public async Task CallAsync_AFailure_CarriesTheMessage()
    {
        var result = await _harness.CallAsync(_reader, "check_health", new { fail = true }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(McpToolCallOutcome.Failed, result.Outcome);
        Assert.Contains("Nothing to check.", result.Error);
    }

    [Fact]
    public async Task CallAsync_AToolTheUserMayNotSee_IsNotAvailable()
    {
        var result = await _harness.CallAsync(_reader, "delete_item", new { item_id = "item-1" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(McpToolCallOutcome.NotAvailable, result.Outcome);
        Assert.Empty(_store.Deleted);
    }

    [Fact]
    public async Task CallAsync_AnUnknownTool_IsNotFound()
    {
        var result = await _harness.CallAsync(_reader, "no_such_tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(McpToolCallOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void McpTestUser_ScopedPermission_IsReadByTheMcpModule()
    {
        var grant = _fashionReader.ToPrincipal().GetMcpPermissionGrant(FixtureTools.ReadPermission);

        Assert.False(grant.IsGlobal);
        Assert.Equal(["fashion"], grant.GetScopeValues(FixtureTools.StoreScopeType));
    }
}

[Trait("Category", "Unit")]
public class McpToolsetConventionsTests
{
    [Fact]
    public void Check_AToolsetThatFollowsTheConventions_HasNoViolations()
    {
        Assert.Empty(McpToolsetConventions.Check([typeof(FixtureTools)]));
    }

    [Fact]
    public void Check_FindsEveryBrokenRule()
    {
        var violations = McpToolsetConventions.Check([typeof(BrokenTools)]);

        var found = violations.Select(x => (x.ToolName, x.Rule)).ToHashSet();
        Assert.Contains(violations, x => x.Rule == McpConventionRules.Name && x.Message.StartsWith("Set Name", StringComparison.Ordinal));
        Assert.Contains(("SearchThings", McpConventionRules.Name), found);
        Assert.Contains(("get_things", McpConventionRules.Description), found);
        Assert.Contains(("get_things", McpConventionRules.ArgumentDescription), found);
        Assert.Contains(("describe_things", McpConventionRules.Scope), found);
        Assert.Contains(("describe_things", McpConventionRules.Access), found);
        Assert.Contains(("list_things", McpConventionRules.Paging), found);
        Assert.Contains(("find_things", McpConventionRules.ReadOnly), found);
        Assert.Contains(("wipe_things", McpConventionRules.DryRun), found);
        Assert.Contains(("wipe_things", McpConventionRules.Safeguard), found);
        Assert.Contains(("grant_things", McpConventionRules.ApprovalCondition), found);
        Assert.Contains(("grant_things", McpConventionRules.ConfirmationMessage), found);
    }

    [Fact]
    public void Check_FindsTwoToolsWithOneNameInAScope()
    {
        var violations = McpToolsetConventions.Check([typeof(FixtureTools), typeof(DuplicateTools)]);

        Assert.Contains(violations, x => x.Rule == McpConventionRules.UniqueName && x.ToolName == "search_items");
    }

    [Fact]
    public void Check_IgnoresADeliberateException()
    {
        var violations = McpToolsetConventions.Check([typeof(BrokenTools)], x => x.Ignore(McpConventionRules.Paging, "list_things", "a fixed list of three"));

        Assert.DoesNotContain(violations, x => x.Rule == McpConventionRules.Paging);
    }

    [Fact]
    public void Verify_ListsEveryViolation()
    {
        var exception = Assert.Throws<McpConventionException>(() => McpToolsetConventions.Verify([typeof(BrokenTools)]));

        Assert.Equal(McpToolsetConventions.Check([typeof(BrokenTools)]).Count, exception.Violations.Count);
        Assert.Contains("wipe_things [safeguard]", exception.Message);
    }

    [Fact]
    public void Ignore_NeedsAReason()
    {
        Assert.Throws<ArgumentException>(() => new McpConventionOptions().Ignore(McpConventionRules.Paging, "list_things", " "));
    }
}
