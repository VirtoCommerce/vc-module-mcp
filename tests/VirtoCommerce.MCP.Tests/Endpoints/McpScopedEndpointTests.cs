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
using ModelContextProtocol.Server;
using Moq;
using OpenIddict.Abstractions;
using VirtoCommerce.MCP.Core;
using VirtoCommerce.MCP.Core.Attributes;
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

    private readonly ConcurrentBag<McpAuditEntry> _auditEntries = [];
    private readonly ConcurrentBag<McpPendingChange> _submittedChanges = [];
    private readonly ConcurrentBag<(McpPendingChange Change, string Result, bool Succeeded)> _completedChanges = [];
    private readonly Mock<IMcpApprovalService> _approvalService = new();
    private readonly ConcurrentBag<McpPendingChangeSearchCriteria> _searchCriteria = [];
    private IHost _host;
    private HttpClient _client;

    public async ValueTask InitializeAsync()
    {
        TestTools.DryRuns = 0;
        TestTools.Executions = 0;
        PartnerTools.Executions = 0;

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
        Assert.Equal(new[] { "canceled_tool", "read_tool", "static_tool" }.Concat(AuthorTools).Order(), ToolNames(reader));
        Assert.Equal(new[] { "canceled_tool", "read_tool", "static_tool", "submit_tool", "write_tool" }.Concat(AuthorTools).Order(), ToolNames(writer));
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
            ToolsCall(ModuleConstants.Tools.ApprovePendingChange, new { change_id = StoredChange.Id }));

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

        var approval = Assert.Single(_auditEntries, x => x.ToolName == ModuleConstants.Tools.ApprovePendingChange);
        Assert.Equal(StoredChange.Id, approval.PendingChangeId);
        Assert.Equal(McpAuditOutcome.Succeeded, approval.Outcome);
    }

    [Fact]
    public async Task ApprovePendingChange_Should_AuditARefusalAsFailed_And_LinkItToTheChange()
    {
        // Act
        var result = await CallAsync(
            Token(permissions: [McpPermissions.Access, McpPermissions.Approve]),
            ToolsCall(ModuleConstants.Tools.ApprovePendingChange, new { change_id = "missing" }));

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
            ToolsCall(ModuleConstants.Tools.GetPendingChange, new { change_id = StoredChange.Id }));

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
            ToolsCall(ModuleConstants.Tools.ApprovePendingChange, new { change_id = StoredChange.Id }));

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
            ToolsCall(ModuleConstants.Tools.ApprovePendingChange, new { change_id = CanceledChange.Id }));

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
        Assert.Contains(ModuleConstants.Tools.ApprovePendingChange, ToolNames(approver));
        Assert.DoesNotContain(ModuleConstants.Tools.ApprovePendingChange, ToolNames(operatorUser));
        Assert.Subset(ToolNames(operatorUser).ToHashSet(), AuthorTools.ToHashSet());
    }

    [Fact]
    public async Task GetPendingChange_Should_ReturnTheRequestersOwnChange_When_TheRequesterIsNotAnApprover()
    {
        // Act
        var result = await CallAsync(Token(user: StoredChange.RequestedByUserId, permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.Tools.GetPendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Contains("submit_tool", Text(result));
    }

    [Fact]
    public async Task GetPendingChange_Should_ReportAnotherUsersChangeAsNotFound_When_TheUserIsNotAnApprover()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.Tools.GetPendingChange, new { change_id = StoredChange.Id }));

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("was not found", Text(result));
    }

    [Fact]
    public async Task RejectPendingChange_Should_LetTheRequesterWithdrawTheirChange_Without_TheApprovePermission()
    {
        // Act
        var result = await CallAsync(Token(user: StoredChange.RequestedByUserId, permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.Tools.RejectPendingChange, new { change_id = StoredChange.Id, reason = "Not needed" }));

        // Assert
        Assert.False(IsError(result), Text(result));
        _approvalService.Verify(x => x.RejectAsync(StoredChange.Id, StoredChange.RequestedByUserId, It.IsAny<string>(), "Not needed"), Times.Once);
    }

    [Fact]
    public async Task RejectPendingChange_Should_Refuse_When_TheUserIsNeitherTheRequesterNorAnApprover()
    {
        // Act
        var result = await CallAsync(Token(permissions: McpPermissions.Access),
            ToolsCall(ModuleConstants.Tools.RejectPendingChange, new { change_id = StoredChange.Id }));

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
        var result = await CallAsync(Token(permissions: permissions), ToolsCall(ModuleConstants.Tools.ListPendingChanges, new { }));

        // Assert
        Assert.False(IsError(result), Text(result));
        Assert.Equal(expectedRequester, Assert.Single(_searchCriteria).RequestedByUserId);
    }

    [Fact]
    public async Task ToolsCall_Should_RefuseAChangeThatNeedsApproval_When_TheScopeHasNoApprovalTools()
    {
        // Act
        var result = await CallAsync(Token(audience: $"{Origin}/{PartnerScope}/mcp"), ToolsCall("partner_submit_tool", new { }), $"/{PartnerScope}/mcp");

        // Assert
        Assert.True(IsError(result));
        Assert.Contains("approval, which is available in the 'admin' scope only", Text(result));
        Assert.Empty(_submittedChanges);
        Assert.Equal(0, PartnerTools.Executions);
        Assert.Equal(McpAuditOutcome.Error, Assert.Single(_auditEntries).Outcome);
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
            foreach (var permission in new[] { McpPermissions.Access, McpPermissions.Approve, WritePermission })
            {
                options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
            }
        });

        services.Configure<McpOptions>(_ => { });
        services.AddSingleton<IMcpScopeRegistry, McpScopeRegistry>();
        services.AddTransient<IMcpPublicOriginResolver, McpPublicOriginResolver>();
        services.AddTransient<McpTokenValidator, ActiveTokenValidator>();
        services.AddTransient<McpToolAuthorizationService>();
        services.AddTransient<McpAuditWriter>();
        services.AddScoped<McpCallToolFilter>();
        services.AddSingleton(Mock.Of<IOpenIddictTokenManager>());
        services.AddSingleton(CreateSettingsManagerWithDefaults());
        services.AddSingleton(CreateAuditEntryService());
        services.AddSingleton(CreateApprovalService());
        services.AddSingleton(CreatePendingChangeService());
        services.AddSingleton(CreatePendingChangeSearchService());

        services.AddMcpScope(new McpScope
        {
            Name = McpScopes.Admin,
            Title = "Test back office",
            Instructions = "Admin instructions",
            RequireAuthenticatedUser = true,
            Permissions = [McpPermissions.Access],
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

        services.AddScopedMcpServer();
        services.AddMcpTools<TestTools>();
        services.AddMcpTools([typeof(UnscopedTools), typeof(StaticTools)]);
        services.AddMcpTools<CanceledTools>();
        services.AddMcpTools<McpApprovalTools>();
        services.AddMcpTools([typeof(PartnerTools)]);
    }

    private static ISettingsManager CreateSettingsManagerWithDefaults()
    {
        return Mock.Of<ISettingsManager>();
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

    private static McpPendingChange[] StoredChanges => [StoredChange, CanceledChange];

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

    private async Task<HttpResponseMessage> PostAsync(string token, object body, string path = "/admin/mcp", bool acceptEventStream = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
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

    private async Task<JsonNode> CallAsync(string token, object body, string path = "/admin/mcp")
    {
        var response = await PostAsync(token, body, path);
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
        return $"{user}|{audience}|{string.Join(',', permissions)}";
    }

    private static string[] AuthorTools =>
        [ModuleConstants.Tools.GetPendingChange, ModuleConstants.Tools.ListPendingChanges, ModuleConstants.Tools.RejectPendingChange];

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

            var parts = header["Bearer ".Length..].Split('|');
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, parts[0]),
                new("sub", parts[0]),
                new("aud", parts[1]),
            };
            claims.AddRange(parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => new Claim("permission", x)));

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
        public static int DryRuns;
        public static int Executions;

        [McpServerTool(Name = "read_tool"), Description("Reads.")]
        public static string ReadTool() => "read";

        [McpServerTool(Name = "write_tool"), Description("Writes.")]
        [Authorize(WritePermission)]
        public static string WriteTool(string value)
        {
            Interlocked.Increment(ref Executions);
            return $"wrote {value}";
        }

        [McpServerTool(Name = "submit_tool"), Description("Activates a promotion after approval.")]
        [Authorize(WritePermission)]
        [McpRequiresApproval]
        public static string SubmitTool(string promotionId, bool dryRun = false)
        {
            if (dryRun)
            {
                Interlocked.Increment(ref DryRuns);
                return $"would activate {promotionId}";
            }

            Interlocked.Increment(ref Executions);
            return $"activated {promotionId}";
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
        public static int Executions;

        [McpServerTool(Name = "partner_submit_tool"), Description("Requires approval in a scope without the approval tools.")]
        [McpRequiresApproval]
        public static string PartnerSubmitTool()
        {
            Interlocked.Increment(ref Executions);
            return "submitted";
        }
    }

    [McpServerToolType]
    public static class UnscopedTools
    {
        [McpServerTool(Name = "unscoped_tool"), Description("Not on any scope.")]
        public static string UnscopedTool() => "unscoped";
    }
}
