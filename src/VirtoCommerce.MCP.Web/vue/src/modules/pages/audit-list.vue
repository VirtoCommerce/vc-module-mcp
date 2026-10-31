<template>
  <VcBlade
    :title="$t('MCP.AUDIT.TITLE')"
    :toolbar-items="bladeToolbar"
    :loading="loading"
    width="50%"
  >
    <VcDataTable
      v-model:search-value="searchValue"
      v-model:sort-field="sortField"
      v-model:sort-order="sortOrder"
      :items="items"
      :total-count="totalCount"
      :pagination="pagination"
      :searchable="true"
      :search-placeholder="$t('MCP.AUDIT.SEARCH')"
      :state-key="STATE_KEY"
      :active-item-id="activeChildParam"
      :show-all-columns="!activeChildParam"
      :global-filters="globalFilters"
      :empty-state="{ icon: 'lucide-scroll-text', title: $t('MCP.AUDIT.EMPTY') }"
      @row-click="(e: { data: McpAuditEntry }) => openDetails(e.data)"
      @pagination-click="pagination.goToPage"
      @filter="onFilter"
    >
      <VcColumn
        id="toolName"
        :title="$t('MCP.FIELDS.TOOL')"
        mobile-role="title"
        :width="220"
        sortable
        always-visible
      />
      <VcColumn
        id="createdDate"
        :title="$t('MCP.FIELDS.DATE')"
        type="datetime"
        mobile-role="field"
        sortable
        always-visible
      />
      <VcColumn
        id="userName"
        :title="$t('MCP.FIELDS.USER')"
        mobile-role="field"
        sortable
      />
      <VcColumn
        id="clientId"
        :title="$t('MCP.FIELDS.CLIENT')"
        sortable
      />
      <VcColumn
        id="scopeName"
        :title="$t('MCP.FIELDS.SCOPE')"
        sortable
      />
      <VcColumn
        id="durationMs"
        :title="$t('MCP.FIELDS.DURATION')"
        type="number"
        sortable
      />
      <VcColumn
        id="outcome"
        :title="$t('MCP.FIELDS.OUTCOME')"
        mobile-role="status"
        :width="190"
        sortable
        always-visible
      >
        <template #body="{ data }">
          <VcStatus :variant="outcomeVariant(data.outcome)">{{ $t(outcomeKey(data.outcome)) }}</VcStatus>
        </template>
      </VcColumn>
    </VcDataTable>
  </VcBlade>
</template>

<script setup lang="ts">
// The audit log: every tool call agents made, with who, which client, the outcome and the trace id.
import {
  notification,
  useAsync,
  useBlade,
  useDataTablePagination,
  useDataTableSort,
  useFunctions,
  useLatestRequest,
  useTableSearch,
  type IBladeToolbar,
} from "@vc-shell/framework";
import { VcBlade, VcColumn, VcDataTable, VcStatus } from "@vc-shell/framework/ui";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { mcpApi, type McpAuditEntry, type McpScopeInfo } from "../api";
import { OUTCOMES, outcomeKey, outcomeVariant } from "../utils";

defineBlade({
  url: "/mcp-audit",
  name: "McpAuditList",
  isWorkspace: true,
  permissions: ["mcp:audit:read"],
  menuItem: {
    title: "MCP.MENU.AUDIT",
    icon: "lucide-scroll-text",
    priority: 20,
  },
});

const STATE_KEY = "MCP_AUDIT";
const PAGE_SIZE = 50;
const TRACE_ID = /^[0-9a-f]{32}$/i;

const { t } = useI18n({ useScope: "global" });
const { activeChildParam, openBlade } = useBlade();
const { debounce } = useFunctions();

const items = ref<McpAuditEntry[]>([]);
const totalCount = ref(0);
const scopes = ref<McpScopeInfo[]>([]);
const filters = ref<{ outcomes?: string[]; scopeName?: string }>({});

const { sortField, sortOrder, sortExpression } = useDataTableSort({
  stateKey: STATE_KEY,
  initialField: "createdDate",
  initialDirection: "DESC",
});
const { searchValue } = useTableSearch({ stateKey: STATE_KEY });
const pagination = useDataTablePagination({ stateKey: STATE_KEY, pageSize: PAGE_SIZE, totalCount });
const search = useLatestRequest();

const { loading, action: load } = useAsync(async () => {
  // the search box takes the trace id an agent reported, or a tool name
  const phrase = searchValue.value?.trim();
  try {
    const result = await search.latest(
      mcpApi.searchAudit({
        ...filters.value,
        traceId: phrase && TRACE_ID.test(phrase) ? phrase.toLowerCase() : undefined,
        toolName: phrase && !TRACE_ID.test(phrase) ? phrase : undefined,
        sort: sortExpression.value,
        skip: pagination.skip,
        take: PAGE_SIZE,
      }),
    );
    if (!result) return;
    items.value = result.results ?? [];
    totalCount.value = result.totalCount ?? 0;
  } catch (error) {
    notification.error(t("MCP.ALERTS.LOAD_FAILED", { message: (error as Error).message }));
  }
});

mcpApi
  .getScopes()
  .then((x) => (scopes.value = x))
  .catch(() => undefined);

const globalFilters = computed(() => [
  {
    id: "outcomes",
    label: t("MCP.FIELDS.OUTCOME"),
    filter: { options: OUTCOMES.map((x) => ({ value: x, label: t(outcomeKey(x)) })), multiple: true },
  },
  {
    id: "scopeName",
    label: t("MCP.FIELDS.SCOPE"),
    filter: { options: scopes.value.map((x) => ({ value: x.name, label: x.title ?? x.name })) },
  },
]);

function onFilter(event: { filters: Record<string, unknown> }) {
  const outcomes = event.filters.outcomes;
  const scopeName = event.filters.scopeName;
  filters.value = {
    outcomes: outcomes ? ([] as string[]).concat(outcomes as string | string[]) : undefined,
    scopeName: (Array.isArray(scopeName) ? scopeName[0] : (scopeName as string)) || undefined,
  };
  if (pagination.skip) pagination.setPage(1);
  else load();
}

function openDetails(entry: McpAuditEntry) {
  openBlade({ name: "McpAuditDetails", param: entry.id, options: { entry } });
}

load();
watch(searchValue, () => pagination.setPage(1));
watch([sortExpression, searchValue, () => pagination.skip], debounce(load, 300));

const bladeToolbar = ref<IBladeToolbar[]>([
  { id: "refresh", title: t("MCP.TOOLBAR.REFRESH"), icon: "lucide-refresh-cw", clickHandler: () => load() },
]);
</script>
