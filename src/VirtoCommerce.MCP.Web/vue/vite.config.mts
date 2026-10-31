import path from "node:path";
import { getDynamicModuleConfiguration } from "@vc-shell/mf-module";

// MCP back office for the Admin Shell host (Module Federation remote): approvals, audit log and tool switches,
// served by the platform at /modules/$(VirtoCommerce.MCP)/plugins/admin-shell/
const base = "/modules/$(VirtoCommerce.MCP)/plugins/admin-shell/";
process.env.APP_BASE_PATH = base;

export default getDynamicModuleConfiguration({
  appId: "admin-shell",
  moduleRoot: path.resolve(__dirname, ".."),
  entry: "./src/modules/index.ts",
  remoteName: "mcp_admin",
  base,
});
