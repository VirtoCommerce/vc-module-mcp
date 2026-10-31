<template>
  <VcBlade
    :title="$t('MCP.TOOLS.TITLE')"
    :toolbar-items="bladeToolbar"
    :loading="loading"
    width="70%"
  >
    <div class="mcp-tools">
      <div class="mcp-tools__scopes">
        <div
          v-for="scope in scopes"
          :key="scope.name"
          class="mcp-tools__scope"
          :class="{ 'mcp-tools__scope--off': scope.isDisabled }"
        >
          <div class="mcp-tools__scope-text">
            <span class="mcp-tools__scope-title">{{ scope.title ?? scope.name }}</span>
            <code class="mcp-tools__muted">{{ scope.path }}</code>
            <span class="mcp-tools__muted">
              {{ scope.isDisabled ? $t("MCP.TOOLS.SCOPE_OFF_HINT") : $t("MCP.TOOLS.SCOPE_ON") }}
            </span>
          </div>
          <VcSwitch
            :model-value="!scope.isDisabled"
            :disabled="saving"
            :aria-label="$t('MCP.TOOLS.SCOPE_ON') + ': ' + scope.name"
            @update:model-value="(on?: boolean) => switchScope(scope, !!on)"
          />
        </div>
      </div>

      <VcDataTable
        v-model:search-value="searchValue"
        :items="visibleTools"
        :total-count="visibleTools.length"
        :searchable="true"
        :state-key="STATE_KEY"
        :global-filters="globalFilters"
        :empty-state="{ icon: 'lucide-wrench', title: $t('MCP.TOOLS.EMPTY') }"
        @filter="onFilter"
      >
        <VcColumn
          id="name"
          :title="$t('MCP.FIELDS.TOOL')"
          mobile-role="title"
          always-visible
        >
          <template #body="{ data }">
            <div class="mcp-tools__tool">
              <span class="mcp-tools__tool-title">{{ data.title || humanize(data.name) }}</span>
              <code class="mcp-tools__muted">{{ data.name }}</code>
            </div>
          </template>
        </VcColumn>
        <VcColumn
          id="scopeName"
          :title="$t('MCP.FIELDS.SCOPE')"
          mobile-role="field"
        />
        <VcColumn
          id="kind"
          :title="$t('MCP.FIELDS.KIND')"
          mobile-role="field"
        >
          <template #body="{ data }">
            <div class="mcp-tools__kinds">
              <span :class="['mcp-tools__chip', data.readOnly ? 'mcp-tools__chip--read' : 'mcp-tools__chip--write']">
                {{ data.readOnly ? $t("MCP.TOOLS.KIND_READ") : $t("MCP.TOOLS.KIND_WRITE") }}
              </span>
              <span
                v-if="data.requiresApproval"
                class="mcp-tools__chip mcp-tools__chip--approval"
              >
                {{ $t("MCP.TOOLS.KIND_APPROVAL") }}
              </span>
            </div>
          </template>
        </VcColumn>
        <VcColumn
          id="description"
          :title="$t('MCP.FIELDS.DESCRIPTION')"
        >
          <template #body="{ data }">
            <span
              class="mcp-tools__description"
              :title="data.description"
              >{{ data.description }}</span
            >
          </template>
        </VcColumn>
        <VcColumn
          id="isEnabled"
          :title="$t('MCP.FIELDS.ENABLED')"
          mobile-role="status"
          always-visible
          :width="120"
        >
          <template #body="{ data }">
            <div
              class="mcp-tools__switch"
              @click.stop
            >
              <VcSwitch
                :model-value="data.isEnabled"
                :disabled="saving || data.disabledBy === 'Scope'"
                :tooltip="data.disabledBy === 'Scope' ? $t('MCP.TOOLS.OFF_BY_SCOPE') : undefined"
                :aria-label="data.name"
                @update:model-value="(on?: boolean) => switchTool(data, !!on)"
              />
            </div>
          </template>
        </VcColumn>
      </VcDataTable>
    </div>
  </VcBlade>
</template>

<script setup lang="ts">
// Tool switches: what agents can use, per scope and per tool. A switch applies to the next agent request, with no
// restart (the MCP.Tools.Disabled setting).
import { notification, useAsync, usePopup, useTableSearch, type IBladeToolbar } from "@vc-shell/framework";
import { VcBlade, VcColumn, VcDataTable, VcSwitch } from "@vc-shell/framework/ui";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { mcpApi, type McpScopeInfo, type McpToolInfo } from "../api";
import { humanize } from "../utils";

defineBlade({
  url: "/mcp-tools",
  name: "McpToolsList",
  isWorkspace: true,
  permissions: ["mcp:tools:manage"],
  menuItem: {
    title: "MCP.MENU.TOOLS",
    icon: "lucide-toggle-right",
    priority: 30,
  },
});

const STATE_KEY = "MCP_TOOLS";

const { t } = useI18n({ useScope: "global" });
const { showConfirmation } = usePopup();
const { searchValue } = useTableSearch({ stateKey: STATE_KEY });

const tools = ref<McpToolInfo[]>([]);
const scopes = ref<McpScopeInfo[]>([]);
const scopeFilter = ref<string>();

const visibleTools = computed(() => {
  const phrase = searchValue.value?.trim().toLowerCase();
  return tools.value.filter(
    (x) =>
      (!scopeFilter.value || x.scopeName === scopeFilter.value) &&
      (!phrase || x.name.includes(phrase) || x.title?.toLowerCase().includes(phrase)),
  );
});

const globalFilters = computed(() => [
  {
    id: "scopeName",
    label: t("MCP.FIELDS.SCOPE"),
    filter: { options: scopes.value.map((x) => ({ value: x.name, label: x.title ?? x.name })) },
  },
]);

function onFilter(event: { filters: Record<string, unknown> }) {
  const value = event.filters.scopeName;
  scopeFilter.value = (Array.isArray(value) ? value[0] : (value as string)) || undefined;
}

const { loading, action: load } = useAsync(async () => {
  try {
    [tools.value, scopes.value] = await Promise.all([mcpApi.getTools(), mcpApi.getScopes()]);
  } catch (error) {
    notification.error(t("MCP.ALERTS.LOAD_FAILED", { message: (error as Error).message }));
  }
});

const { loading: saving, action: save } = useAsync(async (args?: { scopeName: string; toolName: string; on: boolean; label: string }) => {
  try {
    tools.value = await mcpApi.setToolEnabled(args!.scopeName, args!.toolName, args!.on);
    scopes.value = await mcpApi.getScopes();
    notification.success(t(args!.on ? "MCP.TOOLS.SWITCHED_ON" : "MCP.TOOLS.SWITCHED_OFF", { name: args!.label }), { timeout: 2500 });
  } catch (error) {
    notification.error((error as Error).message);
    await load();
  }
});

async function switchTool(tool: McpToolInfo, on: boolean) {
  await save({ scopeName: tool.scopeName, toolName: tool.name, on, label: tool.title || humanize(tool.name) });
}

async function switchScope(scope: McpScopeInfo, on: boolean) {
  if (!on && !(await showConfirmation(t("MCP.TOOLS.CONFIRM_OFF_SCOPE", { scope: scope.title ?? scope.name })))) return;
  await save({ scopeName: scope.name, toolName: "*", on, label: scope.title ?? scope.name });
}

load();

const bladeToolbar = ref<IBladeToolbar[]>([
  { id: "refresh", title: t("MCP.TOOLBAR.REFRESH"), icon: "lucide-refresh-cw", clickHandler: () => load() },
]);
</script>

<style lang="scss">
.mcp-tools {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;

  &__scopes {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
    gap: 12px;
    padding: 16px;
    border-bottom: 1px solid var(--neutrals-200);
  }

  &__scope {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 12px 14px;
    border: 1px solid var(--neutrals-200);
    border-radius: 8px;
    background: var(--additional-50, transparent);

    &--off {
      border-color: var(--danger-300, var(--neutrals-300));
    }
  }

  &__scope-text,
  &__tool {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }

  &__scope-title,
  &__tool-title {
    font-weight: 600;
  }

  &__muted {
    color: var(--neutrals-500);
    font-size: 12px;
  }

  &__kinds {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }

  &__chip {
    padding: 1px 8px;
    border-radius: 10px;
    border: 1px solid currentColor;
    font-size: 11px;
    font-weight: 600;
    line-height: 16px;
    white-space: nowrap;

    &--read {
      color: var(--info-500, var(--neutrals-500));
    }

    &--write {
      color: var(--warning-500, var(--neutrals-600));
    }

    &--approval {
      color: var(--primary-500);
    }
  }

  &__description {
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
    color: var(--neutrals-600);
    font-size: 13px;
  }

  &__switch {
    display: inline-flex;
  }

  @media (max-width: 767px) {
    &__scopes {
      grid-template-columns: 1fr;
      padding: 12px;
    }
  }
}
</style>
