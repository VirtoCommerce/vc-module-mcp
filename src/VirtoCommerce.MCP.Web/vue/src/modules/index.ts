import * as blades from "./pages";
import * as locales from "./locales";
import { defineAppModule } from "@vc-shell/framework";
import type { App } from "vue";
import { registerApprovalsCounter } from "./menuCounter";
import { registerMcpSearch } from "./search";

// The MCP back office: the approval queue, the audit log and the tool switches of the MCP infrastructure.
const appModule = defineAppModule({
  blades,
  locales,
});

export default {
  install(app: App) {
    appModule.install(app);
    registerApprovalsCounter(app);
    // agent tools and connected agents in the Admin Shell's global search
    registerMcpSearch(app);
  },
};
