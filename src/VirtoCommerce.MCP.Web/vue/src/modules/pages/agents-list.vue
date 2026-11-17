<template>
  <VcBlade
    :title="$t('MCP.AGENTS.TITLE')"
    :toolbar-items="bladeToolbar"
    :loading="loading"
    width="70%"
  >
    <VcDataTable
      v-model:search-value="searchValue"
      :items="items"
      :total-count="totalCount"
      :pagination="pagination"
      :searchable="true"
      :search-placeholder="$t('MCP.AGENTS.SEARCH')"
      :state-key="STATE_KEY"
      :global-filters="globalFilters"
      :empty-state="{ icon: 'lucide-plug', title: $t('MCP.AGENTS.EMPTY'), description: $t('MCP.AGENTS.EMPTY_HINT') }"
      @pagination-click="pagination.goToPage"
      @filter="onFilter"
    >
      <VcColumn
        id="clientName"
        :title="$t('MCP.FIELDS.CLIENT')"
        mobile-role="title"
        always-visible
      >
        <template #body="{ data }">
          <div class="mcp-agents__client">
            <span class="mcp-agents__client-name">{{ data.clientName || data.clientId }}</span>
            <code class="mcp-agents__muted">{{ data.clientId }}</code>
            <VcStatus
              v-if="data.registrationType"
              variant="warning"
              class="mcp-agents__registration"
              :title="$t(registrationKey(data.registrationType))"
            >
              {{ $t("MCP.AGENTS.REGISTRATIONS.SELF") }}
            </VcStatus>
          </div>
        </template>
      </VcColumn>
      <VcColumn
        id="userName"
        :title="$t('MCP.FIELDS.USER')"
        mobile-role="field"
        always-visible
      />
      <VcColumn
        id="lastUsedDate"
        :title="$t('MCP.AGENTS.LAST_USED')"
        type="date-ago"
        mobile-role="field"
      />
      <VcColumn
        id="connectedDate"
        :title="$t('MCP.AGENTS.CONNECTED')"
        type="date-ago"
      />
      <VcColumn
        id="activeSessionCount"
        :title="$t('MCP.AGENTS.SESSIONS')"
        type="number"
      />
      <VcColumn
        id="resources"
        :title="$t('MCP.AGENTS.ENDPOINT')"
      >
        <template #body="{ data }">
          <code class="mcp-agents__muted">{{ (data.resources ?? []).join(", ") }}</code>
        </template>
      </VcColumn>
      <VcColumn
        id="actions"
        :title="''"
        mobile-role="status"
        always-visible
        :width="150"
      >
        <template #body="{ data }">
          <VcButton
            size="sm"
            outline
            icon="lucide-unplug"
            :disabled="disconnecting"
            @click.stop="disconnect(data)"
          >
            {{ $t("MCP.AGENTS.DISCONNECT") }}
          </VcButton>
        </template>
      </VcColumn>
    </VcDataTable>
  </VcBlade>
</template>

<script setup lang="ts">
// The AI agents users connected to MCP endpoints: the user's own, or everyone's with mcp:manage. Disconnecting
// revokes the agent's sign-in, so its next request is refused.
import {
  notification,
  useAsync,
  useDataTablePagination,
  useFunctions,
  usePopup,
  useTableSearch,
  type IBladeToolbar,
} from "@vc-shell/framework";
import { VcBlade, VcButton, VcColumn, VcDataTable, VcStatus } from "@vc-shell/framework/ui";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { mcpApi, type McpConnectedAgent } from "../api";
import { requestedAgentSearch } from "../searchRequests";
import { registrationKey } from "../utils";

defineBlade({
  url: "/mcp-agents",
  name: "McpConnectedAgentsList",
  isWorkspace: true,
  menuItem: {
    title: "MCP.MENU.AGENTS",
    icon: "lucide-plug",
    priority: 40,
  },
});

const STATE_KEY = "MCP_AGENTS";
const PAGE_SIZE = 20;

const { t } = useI18n({ useScope: "global" });
const { showConfirmation } = usePopup();
const { debounce } = useFunctions();
const { searchValue } = useTableSearch({ stateKey: STATE_KEY });

// agents found in the global search
watch(
  requestedAgentSearch,
  (keyword) => {
    if (keyword === undefined) return;
    searchValue.value = keyword;
    requestedAgentSearch.value = undefined;
  },
  { immediate: true },
);

const items = ref<McpConnectedAgent[]>([]);
const totalCount = ref(0);
const pagination = useDataTablePagination({ stateKey: STATE_KEY, pageSize: PAGE_SIZE, totalCount });
// true: the agents that registered themselves; false: the ones an administrator registered
const selfRegistered = ref<boolean>();

const { loading, action: load } = useAsync(async () => {
  try {
    const result = await mcpApi.searchConnectedAgents({
      keyword: searchValue.value?.trim() || undefined,
      selfRegistered: selfRegistered.value,
      skip: pagination.skip,
      take: PAGE_SIZE,
    });
    items.value = result.results ?? [];
    totalCount.value = result.totalCount ?? 0;
  } catch (error) {
    notification.error(t("MCP.ALERTS.LOAD_FAILED", { message: (error as Error).message }));
  }
});

const { loading: disconnecting, action: revoke } = useAsync(async (agent?: McpConnectedAgent) => {
  const name = agent!.clientName || agent!.clientId || "";
  try {
    await mcpApi.disconnectAgent(agent!.id);
    notification.success(t("MCP.AGENTS.DISCONNECTED", { agent: name }), { timeout: 2500 });
  } catch (error) {
    notification.error((error as Error).message);
  }
  await load();
});

async function disconnect(agent: McpConnectedAgent) {
  const name = agent.clientName || agent.clientId || "";
  if (await showConfirmation(t("MCP.AGENTS.CONFIRM_DISCONNECT", { agent: name, user: agent.userName ?? "" }))) {
    await revoke(agent);
  }
}

const globalFilters = computed(() => [
  {
    id: "registration",
    label: t("MCP.AGENTS.REGISTRATION"),
    filter: {
      options: [
        { value: "self", label: t("MCP.AGENTS.REGISTRATIONS.SELF") },
        { value: "administrator", label: t("MCP.AGENTS.REGISTRATIONS.ADMINISTRATOR") },
      ],
    },
  },
]);

function onFilter(event: { filters: Record<string, unknown> }) {
  const value = event.filters.registration;
  const registration = Array.isArray(value) ? value[0] : value;
  selfRegistered.value = registration ? registration === "self" : undefined;
}

load();
watch([searchValue, selfRegistered], () => pagination.setPage(1));
watch([searchValue, selfRegistered, () => pagination.skip], debounce(load, 300));

const bladeToolbar = ref<IBladeToolbar[]>([
  { id: "refresh", title: t("MCP.TOOLBAR.REFRESH"), icon: "lucide-refresh-cw", clickHandler: () => load() },
]);
</script>

<style lang="scss">
.mcp-agents {
  &__client {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }

  &__client-name {
    font-weight: 600;
  }

  &__registration {
    align-self: flex-start;
  }

  &__muted {
    color: var(--neutrals-500);
    font-size: 12px;
  }
}
</style>
