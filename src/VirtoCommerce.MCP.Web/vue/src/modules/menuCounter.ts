// The count on the Approvals menu item: changes waiting for the user's review. Registered with the Admin Shell's menu
// counter registry (app.config.globalProperties.$menuCounters), which asks it once the menu arrives and then every
// minute while the tab is visible.
import type { App } from "vue";
import { mcpApi } from "./api";

/** The part of the shell's menu counter registry this module uses */
interface MenuCounters {
  register(counter: {
    itemId: string;
    hint: string;
    permission?: string | string[];
    count(context: { signal: AbortSignal }): Promise<number>;
  }): void;
}

export function registerApprovalsCounter(app: App) {
  const counters = app.config.globalProperties.$menuCounters as MenuCounters | undefined;
  // a vc-shell host without menu counters
  if (!counters) return;
  counters.register({
    itemId: "mcpApprovals",
    hint: "MCP.MENU.COUNTER",
    count: async () => (await mcpApi.getSummary()).pendingChangeCount,
  });
}
