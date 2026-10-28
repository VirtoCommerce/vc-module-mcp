# MCP Sample Module

A minimal module that adds back-office tools through `VirtoCommerce.MCP`. It references only `VirtoCommerce.MCP.Core` and declares the runtime dependency on `VirtoCommerce.MCP` in `module.manifest`. It is used by the [demo walkthrough](../../docs/demo.md).

The tools change only platform users whose name starts with `demo-`.

| Tool | Shows |
| --- | --- |
| `search_users` | A read-only tool with a platform permission and a page size from `IMcpPageSizeResolver` |
| `lock_user`, `unlock_user` | `[McpRequiresApproval]` with a `dryRun` preview |
| `update_user_email` | A `[McpSensitive]` argument |

| File | Demonstrates |
| --- | --- |
| [`Tools/UserTools.cs`](Tools/UserTools.cs) | A tool class with `[McpScope]`, `[Authorize]`, `[McpRequiresApproval]` and `[McpSensitive]` |
| [`Module.cs`](Module.cs) | Registering the tools with `AddMcpTools<T>()` |

The sample keeps its single tool class in the module project to stay small. A real module keeps its MCP code in a project of its own and registers it with `AddMcpToolsFromAssembly`, as described in [Where to put the MCP code of a module](../../README.md#where-to-put-the-mcp-code-of-a-module).

Do not install the sample on a shared or production environment.
