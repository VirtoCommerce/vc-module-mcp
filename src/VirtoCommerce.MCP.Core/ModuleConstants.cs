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
                DefaultValue = 72,
            };

            public static IEnumerable<SettingDescriptor> AllSettings
            {
                get
                {
                    yield return AllowSelfApproval;
                    yield return PendingChangeLifetimeHours;
                }
            }
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

            public static IEnumerable<SettingDescriptor> AllSettings
            {
                get
                {
                    yield return Enabled;
                    yield return LogArguments;
                }
            }
        }

        public static class Cleanup
        {
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
                DefaultValue = 90,
            };

            public static SettingDescriptor PendingChangeRetentionDays { get; } = new()
            {
                Name = "MCP.Cleanup.PendingChangeRetentionDays",
                GroupName = "MCP|Cleanup",
                ValueType = SettingValueType.PositiveInteger,
                DefaultValue = 90,
            };

            public static IEnumerable<SettingDescriptor> AllSettings
            {
                get
                {
                    yield return Enabled;
                    yield return Cron;
                    yield return AuditRetentionDays;
                    yield return PendingChangeRetentionDays;
                }
            }
        }

        public static IEnumerable<SettingDescriptor> AllSettings
        {
            get
            {
                foreach (var setting in Approval.AllSettings)
                {
                    yield return setting;
                }

                foreach (var setting in Audit.AllSettings)
                {
                    yield return setting;
                }

                foreach (var setting in Cleanup.AllSettings)
                {
                    yield return setting;
                }
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
