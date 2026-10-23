using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.MCP.Core;

public static class ModuleConstants
{
    public const string ConfigurationSection = "MCP";

    public static class Security
    {
        public static IReadOnlyList<string> OAuthScopes { get; } = ["openid", "profile", "offline_access"];

        public static class Permissions
        {
            public const string Access = "mcp:access";

            public const string Approve = "mcp:approve";

            public const string AuditRead = "mcp:audit:read";

            public static string[] AllPermissions { get; } =
            [
                Access,
                Approve,
                AuditRead,
            ];
        }
    }

    public static class Settings
    {
        public static class Approval
        {
            public const int DefaultPendingChangeLifetimeHours = 72;

            public static SettingDescriptor AllowSelfApproval { get; } = new()
            {
                Name = "MCP.Approval.AllowSelfApproval",
                GroupName = "MCP|Approval",
                ValueType = SettingValueType.Boolean,
                DefaultValue = false,
            };

            public static SettingDescriptor PendingChangeLifetimeHours { get; } = new()
            {
                Name = "MCP.Approval.PendingChangeLifetimeHours",
                GroupName = "MCP|Approval",
                ValueType = SettingValueType.PositiveInteger,
                DefaultValue = DefaultPendingChangeLifetimeHours,
            };
        }

        public static class Audit
        {
            public static SettingDescriptor Enabled { get; } = new()
            {
                Name = "MCP.Audit.Enabled",
                GroupName = "MCP|Audit",
                ValueType = SettingValueType.Boolean,
                DefaultValue = true,
            };

            public static SettingDescriptor LogArguments { get; } = new()
            {
                Name = "MCP.Audit.LogArguments",
                GroupName = "MCP|Audit",
                ValueType = SettingValueType.Boolean,
                DefaultValue = true,
            };
        }

        public static class Cleanup
        {
            public const int DefaultRetentionDays = 90;

            public static SettingDescriptor Enabled { get; } = new()
            {
                Name = "MCP.Cleanup.Enabled",
                GroupName = "MCP|Cleanup",
                ValueType = SettingValueType.Boolean,
                DefaultValue = true,
            };

            public static SettingDescriptor Cron { get; } = new()
            {
                Name = "MCP.Cleanup.Cron",
                GroupName = "MCP|Cleanup",
                ValueType = SettingValueType.Cron,
                DefaultValue = "0 3 * * *",
            };

            public static SettingDescriptor AuditRetentionDays { get; } = new()
            {
                Name = "MCP.Cleanup.AuditRetentionDays",
                GroupName = "MCP|Cleanup",
                ValueType = SettingValueType.PositiveInteger,
                DefaultValue = DefaultRetentionDays,
            };

            public static SettingDescriptor PendingChangeRetentionDays { get; } = new()
            {
                Name = "MCP.Cleanup.PendingChangeRetentionDays",
                GroupName = "MCP|Cleanup",
                ValueType = SettingValueType.PositiveInteger,
                DefaultValue = DefaultRetentionDays,
            };
        }

        public static IEnumerable<SettingDescriptor> AllSettings
        {
            get
            {
                yield return Approval.AllowSelfApproval;
                yield return Approval.PendingChangeLifetimeHours;
                yield return Audit.Enabled;
                yield return Audit.LogArguments;
                yield return Cleanup.Enabled;
                yield return Cleanup.Cron;
                yield return Cleanup.AuditRetentionDays;
                yield return Cleanup.PendingChangeRetentionDays;
            }
        }
    }

    public static class Jobs
    {
        public const string CleanupJobId = "MCP.Cleanup";
    }

    public static class Tools
    {
        public const string ListPendingChanges = "list_pending_changes";
        public const string GetPendingChange = "get_pending_change";
        public const string ApprovePendingChange = "approve_pending_change";
        public const string RejectPendingChange = "reject_pending_change";
    }
}
