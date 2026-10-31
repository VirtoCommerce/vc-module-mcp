// Statuses of pending changes (McpPendingChangeStatus) and outcomes of tool calls (McpAuditOutcome)
export const CHANGE_STATUSES = ["Pending", "Approved", "Applied", "Failed", "Rejected", "Expired"];
export const OUTCOMES = ["Succeeded", "Failed", "Error", "Forbidden", "PendingApproval", "Disabled"];

type Variant = "success" | "warning" | "danger" | "info" | "primary";

export function changeStatusVariant(status?: string): Variant {
  switch (status) {
    case "Pending":
      return "warning";
    case "Applied":
      return "success";
    case "Failed":
      return "danger";
    case "Approved":
      return "primary";
    default:
      return "info";
  }
}

export function outcomeVariant(outcome?: string): Variant {
  switch (outcome) {
    case "Succeeded":
      return "success";
    case "PendingApproval":
      return "warning";
    case "Failed":
    case "Error":
      return "danger";
    case "Forbidden":
    case "Disabled":
      return "primary";
    default:
      return "info";
  }
}

export const changeStatusKey = (status?: string) => `MCP.STATUS.${status ?? "Pending"}`;
export const outcomeKey = (outcome?: string) => `MCP.OUTCOME.${outcome ?? "Error"}`;

// The dry-run preview of an intent-level tool (McpChangePreview): what it does to which object, as a diff.
export interface ChangePreview {
  action?: string;
  object_type?: string;
  object_id?: string;
  object_name?: string;
  changes?: { field: string; from?: unknown; to?: unknown }[];
  warnings?: string[];
  is_error?: boolean;
  message?: unknown;
}

export function parseJson<T>(json?: string): T | undefined {
  if (!json) return undefined;
  try {
    return JSON.parse(json) as T;
  } catch {
    return undefined;
  }
}

// A preview or a result shaped as McpChangePreview, or nothing when the tool returned something else.
export function asChangePreview(json?: string): ChangePreview | undefined {
  const value = parseJson<ChangePreview>(json);
  return value && typeof value === "object" && (Array.isArray(value.changes) || Array.isArray(value.warnings))
    ? value
    : undefined;
}

export function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

// "activate_promotion" -> "Activate promotion", for a tool without a title
export function humanize(name?: string): string {
  if (!name) return "";
  const text = name.replace(/_/g, " ");
  return text.charAt(0).toUpperCase() + text.slice(1);
}

// "Nov 3, 2026, 10:38 AM" in the user's locale and time zone
export function formatDateTime(value?: string): string {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}
