// Same-origin client of the MCP module's back-office API (api/mcp), typed after the C# models in
// VirtoCommerce.MCP.Core/Models and VirtoCommerce.MCP.Web/Models.

export interface SearchResult<T> {
  results?: T[];
  totalCount?: number;
}

export interface McpScopeInfo {
  name: string;
  title?: string;
  path?: string;
  supportsApproval?: boolean;
  approvalPermission?: string;
  isApprover?: boolean;
  isDisabled?: boolean;
}

export interface McpSummary {
  pendingChangeCount: number;
  failedCallCount: number;
  disabledToolCount: number;
  since?: string;
}

export interface McpPendingChange {
  id: string;
  scopeName?: string;
  toolName?: string;
  arguments?: string;
  status?: string;
  requestedByUserId?: string;
  requestedByUserName?: string;
  reviewedByUserName?: string;
  reviewedDate?: string;
  reviewComment?: string;
  createdDate?: string;
  expirationDate?: string;
}

export interface McpPendingChangeDetails {
  change: McpPendingChange;
  status: string;
  argumentsJson?: string;
  previewJson?: string;
  resultJson?: string;
  canReview: boolean;
  cannotApproveReason?: string;
}

export interface McpAuditEntry {
  id: string;
  createdDate?: string;
  scopeName?: string;
  toolName?: string;
  userId?: string;
  userName?: string;
  clientId?: string;
  arguments?: string;
  outcome?: string;
  errorMessage?: string;
  durationMs?: number;
  pendingChangeId?: string;
  traceId?: string;
}

export interface McpToolInfo {
  scopeName: string;
  name: string;
  title?: string;
  description?: string;
  readOnly?: boolean;
  destructive?: boolean;
  requiresApproval?: boolean;
  isEnabled: boolean;
  disabledBy?: "Tool" | "Scope";
}

export interface PendingChangeCriteria {
  scopeNames?: string[];
  statuses?: string[];
  toolName?: string;
  sort?: string;
  skip?: number;
  take?: number;
}

export interface AuditCriteria {
  scopeName?: string;
  toolName?: string;
  outcomes?: string[];
  traceId?: string;
  pendingChangeId?: string;
  startDate?: string;
  sort?: string;
  skip?: number;
  take?: number;
}

export class McpApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: "same-origin",
    headers: body === undefined ? { Accept: "application/json" } : { "Content-Type": "application/json", Accept: "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) {
    // the review endpoints answer { message } for a refusal the reviewer can act on
    let message = text;
    try {
      message = (JSON.parse(text) as { message?: string }).message ?? text;
    } catch {
      // not JSON
    }
    throw new McpApiError(message || `${response.status} ${response.statusText}`, response.status);
  }
  return (text ? JSON.parse(text) : undefined) as T;
}

export const mcpApi = {
  getScopes: () => request<McpScopeInfo[]>("GET", "/api/mcp/scopes"),
  getSummary: () => request<McpSummary>("GET", "/api/mcp/summary"),
  searchPendingChanges: (criteria: PendingChangeCriteria) =>
    request<SearchResult<McpPendingChange>>("POST", "/api/mcp/pending-changes/search", criteria),
  getPendingChange: (id: string) => request<McpPendingChangeDetails>("GET", `/api/mcp/pending-changes/${encodeURIComponent(id)}`),
  approve: (id: string, comment?: string) =>
    request<McpPendingChangeDetails>("POST", `/api/mcp/pending-changes/${encodeURIComponent(id)}/approve`, { comment }),
  reject: (id: string, comment?: string) =>
    request<McpPendingChangeDetails>("POST", `/api/mcp/pending-changes/${encodeURIComponent(id)}/reject`, { comment }),
  searchAudit: (criteria: AuditCriteria) => request<SearchResult<McpAuditEntry>>("POST", "/api/mcp/audit/search", criteria),
  getTools: () => request<McpToolInfo[]>("GET", "/api/mcp/tools"),
  setToolEnabled: (scopeName: string, toolName: string, enabled: boolean) =>
    request<McpToolInfo[]>("PUT", "/api/mcp/tools/availability", { scopeName, toolName, enabled }),
};
