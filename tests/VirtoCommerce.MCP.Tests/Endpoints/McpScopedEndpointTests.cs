using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
using VirtoCommerce.MCP.Core.Diagnostics;
using VirtoCommerce.MCP.Core.Extensions;
using VirtoCommerce.MCP.Core.Models;
using VirtoCommerce.MCP.Core.Services;
using VirtoCommerce.MCP.Data.Services;
using VirtoCommerce.MCP.Web.Extensions;
using VirtoCommerce.MCP.Web.Filters;
using VirtoCommerce.MCP.Web.Services;
using VirtoCommerce.MCP.Web.Tools;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.Platform.Core.Settings;
using Xunit;

namespace VirtoCommerce.MCP.Tests.Endpoints;

[Trait("Category", "Unit")]
public sealed class McpScopedEndpointTests : IAsyncLifetime
{
    private const string Origin = "http://localhost";
    private const string WritePermission = "test:write";
    private const string PartnerScope = "partner";
    private const string ScopedPermission = "test:scoped";
    private const string StoreScopeType = "TestStoreSelectedScope";
    private const string ShopScope = "shop";
    private const string CategoryScope = "category";
    private const string CategoryApprovePermission = "category:approve";
    private const string ShopHost = "shop.example.test";

    private static readonly string[] _readerTools = ["canceled_tool", "read_tool", "static_tool"];
    private static readonly string[] _writerTools = ["canceled_tool", "conditional_tool", "read_tool", "static_tool", "submit_contact_tool", "submit_tool", "write_tool"];
    private static readonly string[] _policyPermissions = [McpPermissions.Access, McpPermissions.Approve, WritePermission, CategoryApprovePermission];

    private readonly ConcurrentBag<McpAuditEntry> _auditEntries = [];
    private readonly ConcurrentBag<McpPendingChange> _submittedChanges = [];
    private readonly ConcurrentBag<(McpPendingChange Change, string Result, bool Succeeded)> _completedChanges = [];
    private readonly Mock<IMcpApprovalService> _approvalService = new();
    private readonly Mock<ISettingsManager> _settingsManager = new();
    private readonly ConcurrentBag<McpPendingChangeSearchCriteria> _searchCriteria = [];
    private readonly ConcurrentDictionary<string, McpJob> _jobs = new();
    private IHost _host;
    private HttpClient _client;

    public async ValueTask InitializeAsync()
    {
        TestTools.Reset();
        PartnerTools.Reset();

        UseIdentityClaimTypes();

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(ConfigureServices)
                .Configure(app => app.UseMcpScopes()))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Request_Should_ReturnABearerChallengeWithResourceMetadata_When_Anonymous()
    {
        // Act
        var response = await PostAsync(token: null, ToolsList());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains($"resource_metadata=\"{Origin}/.well-known/oauth-protected-resource/admin/mcp\"", challenge);
        Assert.DoesNotContain("error=", challenge);
    }

    [Fact]
    public async Task Request_Should_RejectAToken_When_ItWasIssuedForAnotherResource()
    {
        // Act
        var response = await PostAsync(Token(audience: $"{Origin}/ucp/mcp", McpPermissions.Access), ToolsList());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Request_Should_BeForbidden_When_TheUserLacksTheScopePermission()
    {
        // Act
        var response = await PostAsync(Token(), ToolsList());

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_Should_BeForbidden_When_TheScopePermissionIsNotRegistered()
    {
        // Act
        var response = await PostAsync(Token(audience: $"{Origin}/vendor/mcp", "vendor_portal:access"), ToolsList(), "/vendor/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ToolsList_Should_ShowOnlyTheScopeToolsTheUserMayCall()
    {
        // Act
        var reader = await CallAsync(Token(permissions: McpPermissions.Access), ToolsList());
        var writer = await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsList());

        // Assert
        Assert.Equal(_readerTools.Concat(AuthorTools).Order(), ToolNames(reader));
        Assert.Equal(_writerTools.Concat(AuthorTools).Order(), ToolNames(writer));
    }

    [Fact]
    public async Task Initialize_Should_ReportTheScopeServerInfo()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), Request("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" },
        }));

        // Assert
        Assert.Equal("Test back office", result["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("Admin instructions", result["result"]!["instructions"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_Should_RefuseTheCall_When_TheUserLacksTheToolPermission()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall("write_tool", new { }));

        // Assert
        Assert.True(IsError(result));
        Assert.Equal(0, TestTools.Executions);
    }

    [Fact]
    public async Task ToolsCall_Should_TreatAToolOfAnotherScopeAsUnknown()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall("unscoped_tool", new { }));

        // Assert
        Assert.True(IsError(result));
    }

    [Fact]
    public async Task ToolsCall_Should_RefuseTheCall_When_ARequiredArgumentIsMissing()
    {
        // Act
        var result = await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("write_tool", new { }));

        // Assert
        Assert.True(IsError(result));
        Assert.Equal(0, TestTools.Executions);
        Assert.Equal(McpAuditOutcome.Error, Assert.Single(_auditEntries).Outcome);
    }

    [Fact]
    public async Task ToolsCall_Should_CallAToolOfAStaticClass()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall("static_tool", new { }));

        // Assert
        Assert.False(IsError(result));
        Assert.Equal("static", Text(result));
    }

    [Fact]
    public async Task ToolsCall_Should_AuditTheCall()
    {
        // Act
        await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("write_tool", new { value = "x" }));

        // Assert
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpScopes.Admin, entry.ScopeName);
        Assert.Equal("write_tool", entry.ToolName);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal(McpAuditOutcome.Succeeded, entry.Outcome);
        Assert.Contains("\"value\":\"x\"", entry.Arguments);
        Assert.Equal(1, TestTools.Executions);
    }

    [Fact]
    public async Task ToolsCall_Should_TraceTheCall_And_LinkTheAuditEntryToTheTrace()
    {
        // Arrange
        var activities = new ConcurrentBag<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == McpDiagnostics.ActivitySourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        // Act
        await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("write_tool", new { value = "x" }));

        // Assert
        var activity = Assert.Single(activities, x => (string)x.GetTagItem(McpDiagnostics.ToolTag) == "write_tool");
        Assert.Equal(McpScopes.Admin, activity.GetTagItem(McpDiagnostics.ScopeTag));
        Assert.Equal(McpAuditOutcome.Succeeded, activity.GetTagItem(McpDiagnostics.OutcomeTag));
        Assert.Equal(activity.TraceId.ToString(), Assert.Single(_auditEntries).TraceId);
    }

    [Fact]
    public async Task ToolsCall_Should_SubmitTheChangeWithADryRunPreview_When_TheToolRequiresApproval()
    {
        // Act
        var result = await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("submit_tool", new { promotionId = "p1" }));

        // Assert
        Assert.False(IsError(result));
        Assert.Contains("pending_approval", Text(result));
        Assert.Contains("change-1", Text(result));
        Assert.Equal(1, TestTools.DryRuns);
        Assert.Equal(0, TestTools.Executions);

        var change = Assert.Single(_submittedChanges);
        Assert.Equal("submit_tool", change.ToolName);
        Assert.Equal("user-1", change.RequestedByUserId);
        Assert.Contains("\"promotionId\":\"p1\"", change.Arguments);
        Assert.DoesNotContain("dryRun", change.Arguments);
        Assert.Contains("would activate p1", change.Preview);

        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.PendingApproval, entry.Outcome);
        Assert.Equal("change-1", entry.PendingChangeId);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_ExecuteTheStoredCallOnce()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve, WritePermission]),
            ToolsCall(ModuleConstants.ToolNames.ApprovePendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Equal(1, TestTools.Executions);
        Assert.Equal(0, TestTools.DryRuns);

        var completed = Assert.Single(_completedChanges);
        Assert.True(completed.Succeeded);
        Assert.Contains("activated p2", completed.Result);
        Assert.Equal("user-1", completed.Change.ReviewedByUserId);

        var execution = Assert.Single(_auditEntries, x => x.ToolName == "submit_tool");
        Assert.Equal(StoredChange.Id, execution.PendingChangeId);
        Assert.Equal(McpAuditOutcome.Succeeded, execution.Outcome);
        Assert.Contains("\"promotionId\":\"p2\"", execution.Arguments);

        var approval = Assert.Single(_auditEntries, x => x.ToolName == ModuleConstants.ToolNames.ApprovePendingChange);
        Assert.Equal(StoredChange.Id, approval.PendingChangeId);
        Assert.Equal(McpAuditOutcome.Succeeded, approval.Outcome);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_AuditARefusalAsFailed_And_LinkItToTheChange()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.ApprovePendingChange, new { change_id = "missing" }));

        // Assert
        Assert.True(IsError(result));
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Failed, entry.Outcome);
        Assert.Equal("missing", entry.PendingChangeId);
        Assert.Contains("was not found", entry.ErrorMessage);
    }

    [Fact]
    public async Task GetPendingChange_Should_LinkItsAuditEntryToTheChange()
    {
        // Act
        await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.GetPendingChange, new { change_id = StoredChange.Id }));

        // Assert
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(StoredChange.Id, entry.PendingChangeId);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_Refuse_When_TheApproverMayNotRunTheTool()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.ApprovePendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("not allowed to run tool 'submit_tool'", Text(result));
        Assert.Equal(0, TestTools.Executions);
        _approvalService.Verify(x => x.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ToolsCall_Should_RunADryRunRightAway_When_TheToolRequiresApproval()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, WritePermission]),
            ToolsCall("submit_tool", new { promotionId = "p1", dryRun = true }));

        // Assert
        Assert.False(IsError(result));
        Assert.Contains("would activate p1", Text(result));
        Assert.Equal(1, TestTools.DryRuns);
        Assert.Equal(0, TestTools.Executions);
        Assert.Empty(_submittedChanges);
        Assert.Equal(McpAuditOutcome.Succeeded, Assert.Single(_auditEntries).Outcome);
    }

    [Fact]
    public async Task ToolsCall_Should_AuditARefusedCallAsForbidden()
    {
        // Act
        await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall("submit_tool", new { promotionId = "p1" }));

        // Assert
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Forbidden, entry.Outcome);
        Assert.Empty(_submittedChanges);
        Assert.Equal(0, TestTools.DryRuns);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_RecordTheChangeAsFailed_When_TheToolIsCanceledWhileRunning()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.ApprovePendingChange, new { change_id = CanceledChange.Id }));

        // Assert
        Assert.False(IsError(result), Text(result));
        var completed = Assert.Single(_completedChanges);
        Assert.Equal(CanceledChange.Id, completed.Change.Id);
        Assert.False(completed.Succeeded);
        Assert.Equal(McpAuditOutcome.Error, Assert.Single(_auditEntries, x => x.ToolName == "canceled_tool").Outcome);
    }

    [Fact]
    public async Task ToolsList_Should_HideTheApproveTool_When_TheUserLacksTheApprovePermission()
    {
        // Act
        var approver = await CallAsync(Token(permissions: [McpPermissions.Access, McpPermissions.Approve]), ToolsList());
        var operatorUser = await CallAsync(Token(permissions: McpPermissions.Access), ToolsList());

        // Assert
        Assert.Contains(ModuleConstants.ToolNames.ApprovePendingChange, ToolNames(approver));
        Assert.DoesNotContain(ModuleConstants.ToolNames.ApprovePendingChange, ToolNames(operatorUser));
        Assert.Subset(ToolNames(operatorUser).ToHashSet(), AuthorTools.ToHashSet());
    }

    [Fact]
    public async Task GetPendingChange_Should_ReturnTheRequestersOwnChange_When_TheRequesterIsNotAnApprover()
    {
        // Act
        var result = await CallAsync(Token(user: StoredChange.RequestedByUserId, permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.ToolNames.GetPendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Contains("submit_tool", Text(result));
    }

    [Fact]
    public async Task GetPendingChange_Should_ReportAnotherUsersChangeAsNotFound_When_TheUserIsNotAnApprover()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.ToolNames.GetPendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("was not found", Text(result));
    }

    [Fact]
    public async Task RejectPendingChange_Should_LetTheRequesterWithdrawTheirChange_Without_TheApprovePermission()
    {
        // Act
        var result = await CallAsync(Token(user: StoredChange.RequestedByUserId, permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.ToolNames.RejectPendingChange, new { change_id = StoredChange.Id, reason = "Not needed" }));

        // Assert
        Assert.False(IsError(result), Text(result));
        _approvalService.Verify(x => x.RejectAsync(StoredChange.Id, StoredChange.RequestedByUserId, It.IsAny<string>(), "Not needed"), Times.Once);
    }

    [Fact]
    public async Task RejectPendingChange_Should_Refuse_When_TheUserIsNeitherTheRequesterNorAnApprover()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.ToolNames.RejectPendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.True(IsError(result));
        _approvalService.Verify(x => x.RejectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(false, "user-1")]
    [InlineData(true, null)]
    public async Task ListPendingChanges_Should_ListOnlyTheUsersOwnChanges_When_TheUserIsNotAnApprover(bool approver, string expectedRequester)
    {
        // Arrange
        string[] permissions = approver ? [McpPermissions.Access, McpPermissions.Approve] : [McpPermissions.Access];

        // Act
        var result = await CallAsync(Token(permissions: permissions), ToolsCall(ModuleConstants.ToolNames.ListPendingChanges, new { }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Equal(expectedRequester, Assert.Single(_searchCriteria).RequestedByUserId);
    }

    [Fact]
    public async Task ListPendingChanges_Should_ReturnNoChanges_When_TheUserHasNoId()
    {
        // Act
        var result = await CallAsync(Token(user: "", permissions: McpPermissions.Access), ToolsCall(ModuleConstants.ToolNames.ListPendingChanges, new { }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Empty(_searchCriteria);
        Assert.Contains("\"total_count\":0", Text(result));
    }

    [Fact]
    public async Task ToolsList_Should_NotExposeInjectedServices_InTheInputSchema()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsList());

        // Assert
        var tool = result["result"]!["tools"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == ModuleConstants.ToolNames.ListPendingChanges);
        var properties = tool!["inputSchema"]!["properties"]!.AsObject().Select(x => x.Key).ToList();
        Assert.Contains("take", properties);
        Assert.DoesNotContain(properties, x => x.Contains("Resolver", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(500, 50)]
    public async Task ListPendingChanges_Should_UseTheDefaultPageSize_And_CapIt(int? take, int expectedTake)
    {
        // Act
        var result = await CallAsync(Token(permissions: [McpPermissions.Access]), ToolsCall(ModuleConstants.ToolNames.ListPendingChanges, new { take }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Equal(expectedTake, Assert.Single(_searchCriteria).Take);
    }

    [Fact]
    public async Task ToolsCall_Should_RefuseAChangeThatNeedsApproval_When_TheScopeHasNoApprovalTools()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("partner_submit_tool", new { }), $"/{PartnerScope}/mcp");

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("approval, which is not available in the 'partner' scope", Text(result));
        Assert.Empty(_submittedChanges);
        Assert.Equal(0, PartnerTools.Executions);
        Assert.Equal(McpAuditOutcome.Error, Assert.Single(_auditEntries).Outcome);
    }

    [Fact]
    public async Task ToolsCall_Should_AuditAnUnexpectedExceptionAsError()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("failing_tool", new { }), $"/{PartnerScope}/mcp");

        // Assert
        Assert.True(IsError(result));
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Error, entry.Outcome);
        Assert.Equal(FailingTools.UnexpectedMessage, entry.ErrorMessage);
    }

    [Fact]
    public async Task ToolsCall_Should_AuditARefusalOfTheToolAsFailed_With_ItsOwnMessage()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("refusing_tool", new { }), $"/{PartnerScope}/mcp");

        // Assert
        Assert.True(IsError(result));
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Failed, entry.Outcome);
        Assert.Equal(FailingTools.RefusalMessage, entry.ErrorMessage);
        Assert.Equal(FailingTools.RefusalMessage, Text(result));
    }

    [Fact]
    public async Task GetPendingChange_Should_ShowWhatTheToolReturned_As_ThePreview()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.GetPendingChange, new { change_id = PreviewChange.Id }));

        // Assert
        Assert.False(IsError(result), Text(result));
        var details = JsonNode.Parse(Text(result));
        Assert.Equal("p5", details!["preview"]!["object_id"]!.GetValue<string>());
        Assert.Equal("would activate", details["preview"]!["action"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsList_Should_HideADisabledTool()
    {
        // Arrange
        DisableTools("read_tool");

        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsList());

        // Assert
        Assert.DoesNotContain("read_tool", ToolNames(result));
        Assert.Contains("static_tool", ToolNames(result));
    }

    [Fact]
    public async Task ToolsCall_Should_RefuseADisabledTool_And_AuditItAsDisabled()
    {
        // Arrange
        DisableTools("admin/read_tool");

        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall("read_tool", new { }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("disabled by the administrator", Text(result));
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Disabled, entry.Outcome);
        Assert.Null(entry.Arguments);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_Refuse_When_TheToolIsDisabled()
    {
        // Arrange
        DisableTools("submit_tool");

        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve, WritePermission]),
            ToolsCall(ModuleConstants.ToolNames.ApprovePendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("disabled by the administrator", Text(result));
        _approvalService.Verify(x => x.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ToolsCall_Should_MaskSensitiveArgumentsInTheAudit()
    {
        // Act
        await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("sensitive_tool", new { contactId = "c1", email = "a@b.c" }), $"/{PartnerScope}/mcp");

        // Assert
        var entry = Assert.Single(_auditEntries);
        Assert.Contains("\"contactId\":\"c1\"", entry.Arguments);
        Assert.Contains("\"email\":\"***\"", entry.Arguments);
        Assert.DoesNotContain("a@b.c", entry.Arguments);
    }

    [Fact]
    public async Task ToolsCall_Should_StoreATruncationMarker_When_TheArgumentsAreTooLongForTheAudit()
    {
        // Arrange
        var contactId = new string('x', 40 * 1024);

        // Act
        await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("sensitive_tool", new { contactId, email = "a@b.c" }), $"/{PartnerScope}/mcp");

        // Assert
        using var arguments = JsonDocument.Parse(Assert.Single(_auditEntries).Arguments);
        Assert.True(arguments.RootElement.GetProperty("truncated").GetBoolean());
        Assert.True(arguments.RootElement.GetProperty("length").GetInt32() > 40 * 1024);
    }

    [Fact]
    public async Task ToolsCall_Should_KeepTheSensitiveArgumentsOfASubmittedChange_Only_ForItsExecution()
    {
        // Act
        await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("submit_contact_tool", new { contactId = "c1", email = "a@b.c" }));

        // Assert
        var change = Assert.Single(_submittedChanges);
        Assert.Equal(["email"], change.SensitiveArgumentPaths);
        Assert.Contains("a@b.c", change.Arguments);
        Assert.DoesNotContain("a@b.c", Assert.Single(_auditEntries).Arguments);
    }

    [Fact]
    public async Task GetPendingChange_Should_MaskSensitiveArguments()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.GetPendingChange, new { change_id = SensitiveChange.Id }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.DoesNotContain("a@b.c", Text(result));
        Assert.Contains("***", Text(result));
    }

    [Fact]
    public async Task ToolsList_Should_AnswerWithPlainJson_When_TheClientAcceptsJsonOnly()
    {
        // Act
        var response = await PostAsync(Token(permissions: McpPermissions.Access), ToolsList(), acceptEventStream: false);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("read_tool", ToolNames(result));
    }

    [Fact]
    public async Task ToolsCall_Should_BeNotAcceptable_When_TheClientAcceptsJsonOnly()
    {
        // Act
        var response = await PostAsync(Token(permissions: McpPermissions.Access), ToolsCall("read_tool", new { }), acceptEventStream: false);

        // Assert
        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Fact]
    public async Task ToolsCall_Should_ReturnAToolError_NotAProtocolError_When_ARequiredArgumentIsMissing()
    {
        // Act
        var result = await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("write_tool", new { }));

        // Assert
        Assert.Null(result["error"]);
        Assert.True(result["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("Argument 'value' is required.", Text(result));
    }

    [Fact]
    public async Task ToolsCall_Should_AuditAResourceRefusalAsForbidden()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("forbidden_tool", new { }), $"/{PartnerScope}/mcp");

        // Assert
        Assert.True(IsError(result));
        var entry = Assert.Single(_auditEntries);
        Assert.Equal(McpAuditOutcome.Forbidden, entry.Outcome);
        Assert.Equal(FailingTools.ForbiddenMessage, entry.ErrorMessage);
    }

    [Fact]
    public async Task ToolsList_Should_ListToolsThatRequireAUser_To_AnonymousUsers_But_NotPermissionTools()
    {
        // Act
        var result = await CallAsync(token: null, ToolsList(), $"/{ShopScope}/mcp");

        // Assert
        Assert.Equal(["get_my_orders", "search_products"], ToolNames(result));
    }

    [Fact]
    public async Task ToolsCall_Should_RunAnOpenTool_For_AnAnonymousUser()
    {
        // Act
        var result = await CallAsync(token: null, ToolsCall("search_products", new { }), $"/{ShopScope}/mcp");

        // Assert
        Assert.False(IsError(result));
        Assert.Equal("products", Text(result));
    }

    [Theory]
    [InlineData("get_my_orders")]
    [InlineData("get_my_prices")]
    public async Task ToolsCall_Should_ChallengeAnAnonymousUser_When_TheToolRequiresAUser(string toolName)
    {
        // Act
        var response = await PostAsync(token: null, ToolsCall(toolName, new { }), $"/{ShopScope}/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains($"resource_metadata=\"{Origin}/.well-known/oauth-protected-resource/{ShopScope}/mcp\"", challenge);
        Assert.DoesNotContain("error=", challenge);
        Assert.Contains(toolName, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ToolsCall_Should_RunAToolThatRequiresAUser_For_ASignedInUser()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{ShopScope}/mcp"), ToolsCall("get_my_orders", new { }), $"/{ShopScope}/mcp");

        // Assert
        Assert.False(IsError(result));
        Assert.Equal("orders", Text(result));
    }

    [Fact]
    public async Task Initialize_Should_ReportTheScopeVersion_When_TheScopeSetsIt()
    {
        // Act
        var result = await CallAsync(token: null, Request("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" },
        }), $"/{ShopScope}/mcp");

        // Assert
        Assert.Equal("Test shop", result["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("2.5", result["result"]!["serverInfo"]!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task Request_Should_ServeTheScopeOfTheHost_At_TheMcpPath()
    {
        // Act
        var result = await CallAsync(token: null, ToolsList(), "/mcp", host: ShopHost);

        // Assert
        Assert.Equal(["get_my_orders", "search_products"], ToolNames(result));
    }

    [Fact]
    public async Task Request_Should_ChallengeWithTheHostResource_At_TheMcpPathOfTheHost()
    {
        // Act
        var response = await PostAsync(token: null, ToolsCall("get_my_orders", new { }), "/mcp", host: ShopHost);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains($"resource_metadata=\"http://{ShopHost}/.well-known/oauth-protected-resource/mcp\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Request_Should_RejectATokenOfThePathResource_At_TheMcpPathOfTheHost()
    {
        // Act
        var response = await PostAsync(Token(audience: $"http://{ShopHost}/{ShopScope}/mcp"), ToolsList(), "/mcp", host: ShopHost);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Request_Should_NotServeTheMcpPath_On_AHostWithoutAScope()
    {
        // Act
        var response = await PostAsync(token: null, ToolsList(), "/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("user-1", null, false, true)]
    [InlineData("requester", "user-1", false, true)]
    [InlineData("someone", "requester", true, true)]
    [InlineData("someone", "requester", false, false)]
    public async Task GetJobStatus_Should_ShowAJob_Only_ToItsUsersAndApprovers(string jobUserId, string requestedByUserId, bool approver, bool expectedVisible)
    {
        // Arrange
        AddJob("job-1", McpScopes.Admin, jobUserId, requestedByUserId);
        string[] permissions = approver ? [McpPermissions.Access, McpPermissions.Approve] : [McpPermissions.Access];

        // Act
        var result = await CallAsync(Token(permissions: permissions), ToolsCall(ModuleConstants.ToolNames.GetJobStatus, new { job_id = "job-1" }));

        // Assert
        Assert.Equal(expectedVisible, !IsError(result));
        if (expectedVisible)
        {
            Assert.Contains("\"percent\":25", Text(result));
        }
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(500, true)]
    public async Task ToolsCall_Should_HoldTheCallForApproval_Only_When_TheConditionSaysSo(int count, bool expectedPending)
    {
        // Act
        var result = await CallAsync(Token(permissions: [McpPermissions.Access, WritePermission]), ToolsCall("conditional_tool", new { count }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Equal(expectedPending, Text(result).Contains("pending_approval"));
        Assert.Equal(expectedPending ? 0 : 1, TestTools.Executions);
        Assert.Equal(expectedPending ? McpAuditOutcome.PendingApproval : McpAuditOutcome.Succeeded, Assert.Single(_auditEntries).Outcome);
    }

    [Fact]
    public async Task ToolsList_Should_OfferTheApprovalTools_Only_InScopesWithApproval()
    {
        // Act
        var category = await CallAsync(Token(audience: $"{Origin}/{CategoryScope}/mcp"), ToolsList(), $"/{CategoryScope}/mcp");
        var categoryApprover = await CallAsync(Token(audience: $"{Origin}/{CategoryScope}/mcp", permissions: CategoryApprovePermission), ToolsList(), $"/{CategoryScope}/mcp");
        var partner = await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsList(), $"/{PartnerScope}/mcp");

        // Assert
        Assert.Contains(ModuleConstants.ToolNames.ListPendingChanges, ToolNames(category));
        Assert.DoesNotContain(ModuleConstants.ToolNames.ApprovePendingChange, ToolNames(category));
        Assert.Contains(ModuleConstants.ToolNames.ApprovePendingChange, ToolNames(categoryApprover));
        Assert.DoesNotContain(ModuleConstants.ToolNames.ListPendingChanges, ToolNames(partner));
    }

    [Fact]
    public async Task ToolsCall_Should_HoldAChange_In_ACustomScopeWithApproval()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{CategoryScope}/mcp"), ToolsCall("publish_category", new { categoryId = "c1" }), $"/{CategoryScope}/mcp");

        // Assert
        Assert.Contains("pending_approval", Text(result));
        Assert.Equal(CategoryScope, Assert.Single(_submittedChanges).ScopeName);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_Refuse_AnApproverOfAnotherScope()
    {
        // Act: an admin approver calls the approve tool in the category scope, where approvers need category:approve
        var result = await CallAsync(
            Token(audience: $"{Origin}/{CategoryScope}/mcp", permissions: [McpPermissions.Approve]),
            ToolsCall(ModuleConstants.ToolNames.ApprovePendingChange, new { change_id = StoredChange.Id }),
            $"/{CategoryScope}/mcp");

        // Assert
        Assert.True(IsError(result));
        _approvalService.Verify(x => x.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancelJob_Should_CancelAQueuedJob_So_ItNeverStarts()
    {
        // Arrange
        AddJob("job-3", McpScopes.Admin, "user-1");
        _jobs["job-3"].Status = McpJobStatus.Queued;

        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall(ModuleConstants.ToolNames.CancelJob, new { job_id = "job-3" }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Equal(McpJobStatus.Canceled, _jobs["job-3"].Status);
    }

    [Fact]
    public async Task CancelJob_Should_Refuse_When_TheEngineCannotStopARunningJob()
    {
        // Arrange
        AddJob("job-4", McpScopes.Admin, "user-1");

        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall(ModuleConstants.ToolNames.CancelJob, new { job_id = "job-4" }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("cannot stop it", Text(result));
        Assert.Equal(McpJobStatus.Running, _jobs["job-4"].Status);
    }

    [Fact]
    public async Task GetJobStatus_Should_NotShowAJobOfAnotherScope()
    {
        // Arrange
        AddJob("job-2", McpScopes.Vendor, "user-1");

        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall(ModuleConstants.ToolNames.GetJobStatus, new { job_id = "job-2" }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("was not found", Text(result));
    }

    [Theory]
    [InlineData(ScopedPermission, "all stores")]
    [InlineData(ScopedPermission + "|[{\"Type\":\"" + StoreScopeType + "\",\"Scope\":\"B2B-store\"},{\"Type\":\"OtherScope\",\"Scope\":\"x\"}]", "B2B-store")]
    public async Task ToolsCall_Should_RunAPermissionTool_With_TheGrantedStores(string permissionClaim, string expected)
    {
        // Act
        var list = await CallAsync(Token(permissions: [McpPermissions.Access, permissionClaim]), ToolsList());
        var result = await CallAsync(Token(permissions: [McpPermissions.Access, permissionClaim]), ToolsCall("scoped_tool", new { }));

        // Assert
        Assert.Contains("scoped_tool", ToolNames(list));
        Assert.False(IsError(result), Text(result));
        Assert.Equal(expected, Text(result));
    }

    [Fact]
    public async Task ToolsCall_Should_RefuseAPermissionTool_And_AuditItAsForbidden_When_ThePermissionIsNotGranted()
    {
        // Act
        var list = await CallAsync(Token(permissions: McpPermissions.Access), ToolsList());
        var result = await CallAsync(Token(permissions: McpPermissions.Access), ToolsCall("scoped_tool", new { }));

        // Assert
        Assert.DoesNotContain("scoped_tool", ToolNames(list));
        Assert.True(IsError(result));
        Assert.Equal(McpAuditOutcome.Forbidden, Assert.Single(_auditEntries).Outcome);
    }

    private static void UseIdentityClaimTypes()
    {
        ClaimsPrincipalExtensions.UserIdClaimTypes = ["sub", ClaimTypes.NameIdentifier];
        ClaimsPrincipalExtensions.UserNameClaimTypes = ["name"];
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddRouting();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null);
        services.AddAuthorization(options =>
        {
            foreach (var permission in _policyPermissions)
            {
                options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
            }
        });

        services.Configure<McpOptions>(options => options.Scopes[ShopScope] = new McpScopeOptions { Hosts = [ShopHost] });
        services.AddSingleton<IMcpScopeRegistry, McpScopeRegistry>();
        services.AddTransient<IMcpPublicOriginResolver, McpPublicOriginResolver>();
        services.AddTransient<IMcpPageSizeResolver, McpPageSizeResolver>();
        services.AddTransient<McpTokenValidator, ActiveTokenValidator>();
        services.AddTransient<McpChallengeWriter>();
        services.AddTransient<McpToolAuthorizationService>();
        services.AddTransient<McpAuditWriter>();
        services.AddTransient<McpToolAvailabilityService>();
        services.AddTransient<McpPendingChangeExecutor>();
        services.AddScoped<McpCallToolFilter>();
        services.AddSingleton(Mock.Of<IOpenIddictTokenManager>());
        services.AddSingleton(_settingsManager.Object);
        services.AddSingleton(CreateAuditEntryService());
        services.AddSingleton(CreateApprovalService());
        services.AddSingleton(CreatePendingChangeService());
        services.AddSingleton(CreatePendingChangeSearchService());
        services.AddSingleton(CreateJobService());

        services.AddMcpScope(new McpScope
        {
            Name = McpScopes.Admin,
            Title = "Test back office",
            Instructions = "Admin instructions",
            RequireAuthenticatedUser = true,
            Permissions = [McpPermissions.Access],
            ApprovalPermission = McpPermissions.Approve,
        });

        // A custom scope with approval and an approver permission of its own.
        services.AddMcpScope(new McpScope
        {
            Name = CategoryScope,
            RequireAuthenticatedUser = true,
            ApprovalPermission = CategoryApprovePermission,
        });

        services.AddMcpScope(new McpScope
        {
            Name = McpScopes.Vendor,
            RequireAuthenticatedUser = true,
            Permissions = ["vendor_portal:access"],
        });

        services.AddMcpScope(new McpScope
        {
            Name = PartnerScope,
            RequireAuthenticatedUser = true,
        });

        services.AddMcpScope(new McpScope
        {
            Name = ShopScope,
            Title = "Test shop",
            Version = "2.5",
            RequireAuthenticatedUser = false,
        });

        services.AddScopedMcpServer();
        services.AddMcpTools<TestTools>();
        services.AddMcpTools([typeof(UnscopedTools), typeof(StaticTools)]);
        services.AddMcpTools<CanceledTools>();
        services.AddMcpTools([typeof(ScopedPermissionTools)]);
        services.AddMcpTools<McpApprovalTools>();
        services.AddMcpTools<McpJobTools>();
        services.AddMcpTools([typeof(PartnerTools), typeof(FailingTools), typeof(SensitiveTools), typeof(ShopTools), typeof(CategoryTools)]);
    }

    private void DisableTools(string value)
    {
        var setting = ModuleConstants.Settings.Tools.Disabled;
        _settingsManager
            .Setup(x => x.GetObjectSettingAsync(setting.Name, null, null))
            .ReturnsAsync(new ObjectSettingEntry { Name = setting.Name, Value = value });
    }

    private IMcpAuditEntryService CreateAuditEntryService()
    {
        var service = new Mock<IMcpAuditEntryService>();
        service
            .Setup(x => x.SaveChangesAsync(It.IsAny<IList<McpAuditEntry>>()))
            .Callback<IList<McpAuditEntry>>(entries => entries.ToList().ForEach(_auditEntries.Add))
            .Returns(Task.CompletedTask);

        return service.Object;
    }

    private IMcpApprovalService CreateApprovalService()
    {
        _approvalService
            .Setup(x => x.CanReviewAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<McpPendingChange>()))
            .ReturnsAsync(true);

        _approvalService
            .Setup(x => x.SubmitAsync(It.IsAny<McpPendingChange>()))
            .ReturnsAsync((McpPendingChange change) =>
            {
                change.Id = "change-1";
                change.ExpirationDate = DateTime.UtcNow.AddHours(1);
                _submittedChanges.Add(change);
                return change;
            });

        _approvalService
            .Setup(x => x.ApproveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string changeId, string reviewerUserId, string _, string _) =>
            {
                var change = (McpPendingChange)StoredChanges.Single(x => x.Id == changeId).Clone();
                change.Status = McpPendingChangeStatus.Approved;
                change.ReviewedByUserId = reviewerUserId;
                return change;
            });

        _approvalService
            .Setup(x => x.CompleteAsync(It.IsAny<McpPendingChange>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((McpPendingChange change, string result, bool succeeded) =>
            {
                _completedChanges.Add((change, result, succeeded));
                change.Status = succeeded ? McpPendingChangeStatus.Applied : McpPendingChangeStatus.Failed;
                change.Result = result;
                return change;
            });

        _approvalService
            .Setup(x => x.RejectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string changeId, string reviewerUserId, string _, string reason) =>
            {
                var change = (McpPendingChange)StoredChanges.Single(x => x.Id == changeId).Clone();
                change.Status = McpPendingChangeStatus.Rejected;
                change.ReviewedByUserId = reviewerUserId;
                change.ReviewComment = reason;
                return change;
            });

        return _approvalService.Object;
    }

    private IMcpPendingChangeSearchService CreatePendingChangeSearchService()
    {
        var service = new Mock<IMcpPendingChangeSearchService>();
        service
            .Setup(x => x.SearchAsync(It.IsAny<McpPendingChangeSearchCriteria>(), It.IsAny<bool>()))
            .ReturnsAsync((McpPendingChangeSearchCriteria criteria, bool _) =>
            {
                _searchCriteria.Add(criteria);
                return new McpPendingChangeSearchResult { Results = [] };
            });

        return service.Object;
    }

    private IMcpJobService CreateJobService()
    {
        var service = new Mock<IMcpJobService>();
        service
            .Setup(x => x.SaveChangesAsync(It.IsAny<IList<McpJob>>()))
            .Callback<IList<McpJob>>(jobs => jobs.ToList().ForEach(job => _jobs[job.Id] = job))
            .Returns(Task.CompletedTask);
        service
            .Setup(x => x.GetAsync(It.IsAny<IList<string>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((IList<string> ids, string _, bool _) => ids
                .Where(_jobs.ContainsKey)
                .Select(id => (McpJob)_jobs[id].Clone())
                .ToList());

        return service.Object;
    }

    private void AddJob(string id, string scopeName, string userId, string requestedByUserId = null)
    {
        _jobs[id] = new McpJob
        {
            Id = id,
            ScopeName = scopeName,
            ToolName = "generate_coupons",
            Title = "Generate coupons",
            Status = McpJobStatus.Running,
            UserId = userId,
            RequestedByUserId = requestedByUserId,
            ProcessedCount = 250,
            TotalCount = 1000,
        };
    }

    private static IMcpPendingChangeService CreatePendingChangeService()
    {
        var service = new Mock<IMcpPendingChangeService>();
        service
            .Setup(x => x.GetAsync(It.IsAny<IList<string>>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((IList<string> ids, string _, bool _) => StoredChanges
                .Where(x => ids.Contains(x.Id))
                .Select(x => (McpPendingChange)x.Clone())
                .ToList());

        return service.Object;
    }

    private static McpPendingChange StoredChange { get; } = CreateStoredChange("change-2", "submit_tool", "{\"promotionId\":\"p2\"}");

    private static McpPendingChange CanceledChange { get; } = CreateStoredChange("change-3", "canceled_tool", "{}");

    private static McpPendingChange SensitiveChange { get; } = CreateSensitiveChange();

    private static McpPendingChange PreviewChange { get; } = CreatePreviewChange();

    private static McpPendingChange[] StoredChanges => [StoredChange, CanceledChange, SensitiveChange, PreviewChange];

    private static McpPendingChange CreatePreviewChange()
    {
        var change = CreateStoredChange("change-5", "submit_tool", "{\"promotionId\":\"p5\"}");
        change.Preview = "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"action\\\":\\\"would activate\\\",\\\"object_id\\\":\\\"p5\\\"}\"}]}";

        return change;
    }

    private static McpPendingChange CreateSensitiveChange()
    {
        var change = CreateStoredChange("change-4", "submit_contact_tool", "{\"contactId\":\"c1\",\"email\":\"a@b.c\"}");
        change.SensitiveArgumentPaths = ["email"];

        return change;
    }

    private static McpPendingChange CreateStoredChange(string id, string toolName, string arguments)
    {
        return new McpPendingChange
        {
            Id = id,
            ScopeName = McpScopes.Admin,
            ToolName = toolName,
            Arguments = arguments,
            Status = McpPendingChangeStatus.Pending,
            RequestedByUserId = "requester",
            ExpirationDate = DateTime.UtcNow.AddHours(1),
        };
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body, string path = "/admin/mcp", bool acceptEventStream = true, string host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (host != null)
        {
            request.Headers.Host = host;
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (acceptEventStream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<JsonNode> CallAsync(string token, object body, string path = "/admin/mcp", string host = null)
    {
        var response = await PostAsync(token, body, path, host: host);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var data = payload
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .LastOrDefault() ?? payload;

        return JsonNode.Parse(data);
    }

    private static object ToolsList() => Request("tools/list", new { });

    private static object ToolsCall(string name, object arguments) => Request("tools/call", new { name, arguments });

    private static object Request(string method, object parameters) => new { jsonrpc = "2.0", id = 1, method, @params = parameters };

    private static string[] ToolNames(JsonNode response)
    {
        return response["result"]!["tools"]!.AsArray().Select(x => x!["name"]!.GetValue<string>()).Order().ToArray();
    }

    private static bool IsError(JsonNode response)
    {
        return response["error"] != null || response["result"]?["isError"]?.GetValue<bool>() == true;
    }

    private static string Text(JsonNode response)
    {
        return string.Join("\n", response["result"]!["content"]!.AsArray().Select(x => x!["text"]?.GetValue<string>()));
    }

    private static string Token(string audience = $"{Origin}/admin/mcp", string user = "user-1", params string[] permissions)
    {
        return $"{user}|{audience}|{string.Join(';', permissions)}";
    }

    private static string[] AuthorTools =>
        [ModuleConstants.ToolNames.CancelJob, ModuleConstants.ToolNames.GetJobStatus, ModuleConstants.ToolNames.GetPendingChange, ModuleConstants.ToolNames.ListPendingChanges, ModuleConstants.ToolNames.RejectPendingChange];

    private static class McpPermissions
    {
        public const string Access = ModuleConstants.Security.Permissions.Access;
        public const string Approve = ModuleConstants.Security.Permissions.Approve;
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header["Bearer ".Length..].Split('|', 3);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, parts[0]),
                new("sub", parts[0]),
                new("aud", parts[1]),
            };
            claims.AddRange(parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => new Claim("permission", x)));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class ActiveTokenValidator : McpTokenValidator
    {
        public ActiveTokenValidator(IOpenIddictTokenManager tokenManager)
            : base(tokenManager)
        {
        }

        public override Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    [McpServerToolType]
    [McpScope(McpScopes.Admin)]
    public sealed class TestTools
    {
        private static int _dryRuns;
        private static int _executions;

        public static int DryRuns => _dryRuns;

        public static int Executions => _executions;

        public static void Reset()
        {
            _dryRuns = 0;
            _executions = 0;
        }

        [McpServerTool(Name = "read_tool"), Description("Reads.")]
        public static string ReadTool() => "read";

        [McpServerTool(Name = "write_tool"), Description("Writes.")]
        [Authorize(WritePermission)]
        public static string WriteTool(string value)
        {
            Interlocked.Increment(ref _executions);
            return $"wrote {value}";
        }

        [McpServerTool(Name = "submit_contact_tool"), Description("Changes the email of a contact after approval.")]
        [Authorize(WritePermission)]
        [McpRequiresApproval]
        public static string SubmitContactTool(string contactId, [McpSensitive] string email) => $"updated {contactId}";

        [McpServerTool(Name = "conditional_tool"), Description("Generates coupons; more than 100 need approval.")]
        [Authorize(WritePermission)]
        [McpRequiresApproval(Condition = nameof(NeedsApproval))]
        public static string ConditionalTool(int count)
        {
            Interlocked.Increment(ref _executions);
            return $"generated {count}";
        }

        public static bool NeedsApproval(IReadOnlyDictionary<string, JsonElement> arguments)
        {
            return arguments.TryGetValue("count", out var count) && count.GetInt32() > 100;
        }

        [McpServerTool(Name = "submit_tool"), Description("Activates a promotion after approval.")]
        [Authorize(WritePermission)]
        [McpRequiresApproval]
        public static string SubmitTool(string promotionId, bool dryRun = false)
        {
            if (dryRun)
            {
                Interlocked.Increment(ref _dryRuns);
                return $"would activate {promotionId}";
            }

            Interlocked.Increment(ref _executions);
            return $"activated {promotionId}";
        }
    }

    [McpServerToolType]
    [McpScope(McpScopes.Admin)]
    public static class ScopedPermissionTools
    {
        [McpServerTool(Name = "scoped_tool"), Description("Needs a permission that may be limited to stores.")]
        [McpPermission(ScopedPermission)]
        public static string ScopedTool(RequestContext<CallToolRequestParams> context)
        {
            var grant = context.User.GetMcpPermissionGrant(ScopedPermission);
            return grant.IsGlobal ? "all stores" : string.Join(",", grant.GetScopeValues(StoreScopeType));
        }
    }

    [McpServerToolType]
    [McpScope(McpScopes.Admin)]
    public sealed class CanceledTools
    {
        [McpServerTool(Name = "canceled_tool"), Description("Gets canceled while it runs.")]
        [McpRequiresApproval]
        public static string CanceledTool() => throw new OperationCanceledException();
    }

    [McpServerToolType]
    [McpScope(McpScopes.Admin)]
    public static class StaticTools
    {
        [McpServerTool(Name = "static_tool"), Description("Declared in a static class, as SDK tools usually are.")]
        public static string StaticTool() => "static";
    }

    [McpServerToolType]
    [McpScope(PartnerScope)]
    public static class PartnerTools
    {
        private static int _executions;

        public static int Executions => _executions;

        public static void Reset()
        {
            _executions = 0;
        }

        [McpServerTool(Name = "partner_submit_tool"), Description("Requires approval in a scope without the approval tools.")]
        [McpRequiresApproval]
        public static string PartnerSubmitTool()
        {
            Interlocked.Increment(ref _executions);
            return "submitted";
        }
    }

    [McpServerToolType]
    [McpScope(PartnerScope)]
    public static class SensitiveTools
    {
        [McpServerTool(Name = "sensitive_tool"), Description("Takes a sensitive argument.")]
        public static string SensitiveTool(string contactId, [McpSensitive] string email) => $"ok {contactId}";
    }

    [McpServerToolType]
    [McpScope(PartnerScope)]
    public static class FailingTools
    {
        public const string UnexpectedMessage = "The database is not available.";
        public const string RefusalMessage = "Promotion 'p1' was not found.";

        [McpServerTool(Name = "failing_tool"), Description("Fails with an unexpected exception.")]
        public static string FailingTool() => throw new InvalidOperationException(UnexpectedMessage);

        [McpServerTool(Name = "refusing_tool"), Description("Refuses the request.")]
        public static string RefusingTool() => throw new McpException(RefusalMessage);

        public const string ForbiddenMessage = "Promotion p1 belongs to a store the user does not manage.";

        [McpServerTool(Name = "forbidden_tool"), Description("Refuses a resource the user may not manage.")]
        public static string ForbiddenTool() => throw new McpForbiddenException(ForbiddenMessage);
    }

    [McpServerToolType]
    [McpScope(CategoryScope)]
    public static class CategoryTools
    {
        [McpServerTool(Name = "publish_category"), Description("Publishes a category after approval.")]
        [McpRequiresApproval]
        public static string PublishCategory(string categoryId) => $"published {categoryId}";
    }

    [McpServerToolType]
    [McpScope(ShopScope)]
    public static class ShopTools
    {
        [McpServerTool(Name = "search_products"), Description("Open to everyone.")]
        public static string SearchProducts() => "products";

        [McpServerTool(Name = "get_my_orders"), Description("Listed to everyone, needs a signed-in buyer.")]
        [McpRequiresUser]
        public static string GetMyOrders() => "orders";

        [McpServerTool(Name = "get_my_prices"), Description("Needs a permission.")]
        [Authorize(WritePermission)]
        public static string GetMyPrices() => "prices";
    }

    [McpServerToolType]
    public static class UnscopedTools
    {
        [McpServerTool(Name = "unscoped_tool"), Description("Not on any scope.")]
        public static string UnscopedTool() => "unscoped";
    }
}
