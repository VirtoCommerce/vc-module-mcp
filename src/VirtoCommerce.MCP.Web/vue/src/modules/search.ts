// Agent tools and connected agents in the Admin Shell's global search (the header box, Ctrl+K): this module's providers,
// registered with the shell's registry (app.config.globalProperties.$globalSearch; see the shell's docs/global-search.md).
// A result opens its list with the search filled in, as a user would find it there.
import type { App } from "vue";
import { mcpApi, type McpToolInfo } from "./api";
import { requestedAgentSearch, requestedToolSearch } from "./searchRequests";
import { humanize } from "./utils";

/** The part of the shell's global search registry this module uses */
interface GlobalSearch {
  registerProvider(provider: object): void;
  rankItems<T>(items: T[], query: string, fields: (item: T) => unknown[], take?: number): T[];
  translate(keyOrText: string): string;
  openBlade(options: { route: string; workspace: string }): Promise<void>;
}

interface SearchContext {
  take: number;
  signal: AbortSignal;
}

// The tools change only when someone switches one: a short cache keeps the box fast and the badges fresh enough.
const TOOLS_CACHE_MS = 30_000;

/** Registers the Agent tools and Connected agents search providers; nothing in a host without global search */
export function registerMcpSearch(app: App) {
  const search = app.config.globalProperties.$globalSearch as GlobalSearch | undefined;
  if (!search) return;

  const openTools = (keyword: string) => {
    requestedToolSearch.value = keyword;
    return search.openBlade({ route: "/mcp-tools", workspace: "McpToolsList" });
  };
  const openAgents = (keyword: string) => {
    requestedAgentSearch.value = keyword;
    return search.openBlade({ route: "/mcp-agents", workspace: "McpConnectedAgentsList" });
  };

  let tools: { loaded: number; items: Promise<McpToolInfo[]> } | undefined;
  const loadTools = () => {
    if (!tools || Date.now() - tools.loaded > TOOLS_CACHE_MS) {
      const items = mcpApi.getTools().catch((error) => {
        // the next search asks again
        tools = undefined;
        throw error;
      });
      tools = { loaded: Date.now(), items };
    }
    return tools.items;
  };

  search.registerProvider({
    id: "mcp.tools",
    title: "MCP.SEARCH.TOOLS",
    icon: "lucide-wrench",
    order: 160,
    permission: "mcp:manage",
    aliases: ["tool", "tools"],
    search: async (query: string, context: SearchContext) => {
      const all = await loadTools();
      const ranked = search.rankItems(all, query, (x) => [x.title, x.name, humanize(x.name), x.description, x.scopeName]);
      return {
        total: ranked.length,
        showAll: () => openTools(query),
        items: ranked.slice(0, context.take).map((tool) => ({
          id: `${tool.scopeName}/${tool.name}`,
          title: tool.title || humanize(tool.name),
          description: [tool.name, tool.scopeName].join(" · "),
          icon: "lucide-wrench",
          badge: !tool.isEnabled
            ? search.translate("MCP.SEARCH.OFF")
            : tool.requiresApproval
              ? search.translate("MCP.TOOLS.KIND_APPROVAL")
              : undefined,
          execute: () => openTools(tool.name),
        })),
      };
    },
  });

  // Every signed-in user finds their own agents; with mcp:manage, everyone's (the server decides).
  search.registerProvider({
    id: "mcp.agents",
    title: "MCP.SEARCH.AGENTS",
    icon: "lucide-plug",
    order: 161,
    minLength: 2,
    delay: 250,
    aliases: ["agent", "agents"],
    search: async (query: string, context: SearchContext) => {
      const result = await mcpApi.searchConnectedAgents({ keyword: query, take: context.take }, context.signal);
      return {
        total: result.totalCount,
        showAll: () => openAgents(query),
        items: (result.results ?? []).map((agent) => ({
          id: agent.id,
          title: agent.clientName || agent.clientId,
          description: [agent.userName, agent.clientId].filter(Boolean).join(" · "),
          icon: "lucide-plug",
          meta: agent.lastUsedDate ? new Date(agent.lastUsedDate).toLocaleDateString() : undefined,
          execute: () => openAgents(query),
        })),
      };
    },
  });
}
