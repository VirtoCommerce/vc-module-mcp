<template>
  <VcBlade
    :title="$t('MCP.APPROVALS.TITLE')"
    :toolbar-items="bladeToolbar"
    :loading="loading"
    width="50%"
  >
    <VcBanner
      v-if="scopesLoaded && !reviewScopes.length"
      variant="info"
      icon="lucide-info"
      class="mcp-approvals__banner"
    >
      {{ $t("MCP.APPROVALS.NOT_APPROVER") }}
    </VcBanner>
    <VcDataTable
      v-else
      v-model:sort-field="sortField"
      v-model:sort-order="sortOrder"
      :items="items"
      :total-count="totalCount"
      :pagination="pagination"
      :state-key="STATE_KEY"
      :active-item-id="activeChildParam"
      :show-all-columns="!activeChildParam"
      :global-filters="globalFilters"
      :empty-state="{ icon: 'lucide-shield-check', title: $t('MCP.APPROVALS.EMPTY'), description: $t('MCP.APPROVALS.EMPTY_HINT') }"
      @row-click="(e: { data: McpPendingChange }) => openDetails(e.data)"
      @pagination-click="pagination.goToPage"
      @filter="onFilter"
    >
      <VcColumn
        id="toolName"
        :title="$t('MCP.FIELDS.TOOL')"
        mobile-role="title"
        sortable
        always-visible
      >
        <template #body="{ data }">
          <div class="mcp-approvals__tool">
            <span class="mcp-approvals__tool-title">{{ humanize(data.toolName) }}</span>
            <code class="mcp-approvals__tool-name">{{ data.toolName }}</code>
          </div>
        </template>
      </VcColumn>
      <VcColumn
        id="requestedByUserName"
        :title="$t('MCP.FIELDS.REQUESTED_BY')"
        mobile-role="field"
        sortable
      />
      <VcColumn
        id="createdDate"
        :title="$t('MCP.FIELDS.REQUESTED_AT')"
        type="date-ago"
        mobile-role="field"
        sortable
        always-visible
      />
      <VcColumn
        id="expirationDate"
        :title="$t('MCP.FIELDS.EXPIRES')"
        type="datetime"
        sortable
      />
      <VcColumn
        id="scopeName"
        :title="$t('MCP.FIELDS.SCOPE')"
        sortable
      />
      <VcColumn
        id="status"
        :title="$t('MCP.FIELDS.STATUS')"
        mobile-role="status"
        sortable
        always-visible
      >
        <template #body="{ data }">
          <VcStatus :variant="changeStatusVariant(data.status)">{{ $t(changeStatusKey(data.status)) }}</VcStatus>
        </template>
      </VcColumn>
    </VcDataTable>
  </VcBlade>
</template>

<script setup lang="ts">
// The approval queue: changes agents asked to make, in the scopes where the user is an approver. Pending first.
import {
  notification,
  useAsync,
  useBlade,
  useDataTablePagination,
  useDataTableSort,
  useFunctions,
  useLatestRequest,
  type IBladeToolbar,
} from "@vc-shell/framework";
import { VcBanner, VcBlade, VcColumn, VcDataTable, VcStatus } from "@vc-shell/framework/ui";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { mcpApi, type McpPendingChange, type McpScopeInfo } from "../api";
import { CHANGE_STATUSES, changeStatusKey, changeStatusVariant, humanize } from "../utils";

defineBlade({
  url: "/mcp-approvals",
  name: "McpApprovalsList",
  isWorkspace: true,
  menuItem: {
    title: "MCP.MENU.APPROVALS",
    icon: "lucide-shield-check",
    priority: 10,
  },
});

const STATE_KEY = "MCP_APPROVALS";
const PAGE_SIZE = 20;

const { t } = useI18n({ useScope: "global" });
const { activeChildParam, openBlade, exposeToChildren } = useBlade();
const { debounce } = useFunctions();

const items = ref<McpPendingChange[]>([]);
const totalCount = ref(0);
const reviewScopes = ref<McpScopeInfo[]>([]);
const scopesLoaded = ref(false);
// no status filter: the queue, the changes waiting for a decision
const filters = ref<{ statuses: string[]; scopeNames?: string[] }>({ statuses: ["Pending"] });

const { sortField, sortOrder, sortExpression } = useDataTableSort({
  stateKey: STATE_KEY,
  initialField: "createdDate",
  initialDirection: "DESC",
});
const pagination = useDataTablePagination({ stateKey: STATE_KEY, pageSize: PAGE_SIZE, totalCount });
const search = useLatestRequest();

const { loading, action: load } = useAsync(async () => {
  if (!scopesLoaded.value) {
    reviewScopes.value = (await mcpApi.getScopes()).filter((x) => x.isApprover);
    scopesLoaded.value = true;
  }
  if (!reviewScopes.value.length) return;
  try {
    const result = await search.latest(
      mcpApi.searchPendingChanges({
        ...filters.value,
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

const globalFilters = computed(() => [
  {
    id: "statuses",
    label: t("MCP.FIELDS.STATUS"),
    filter: { options: CHANGE_STATUSES.map((x) => ({ value: x, label: t(changeStatusKey(x)) })), multiple: true },
  },
  ...(reviewScopes.value.length > 1
    ? [
        {
          id: "scopeNames",
          label: t("MCP.FIELDS.SCOPE"),
          filter: { options: reviewScopes.value.map((x) => ({ value: x.name, label: x.title ?? x.name })), multiple: true },
        },
      ]
    : []),
]);

function toList(value: unknown): string[] | undefined {
  if (value === undefined || value === null || value === "") return undefined;
  const list = ([] as string[]).concat(value as string | string[]).filter(Boolean);
  return list.length ? list : undefined;
}

function onFilter(event: { filters: Record<string, unknown> }) {
  filters.value = {
    statuses: toList(event.filters.statuses) ?? ["Pending"],
    scopeNames: toList(event.filters.scopeNames),
  };
  if (pagination.skip) pagination.setPage(1);
  else load();
}

function openDetails(item: McpPendingChange) {
  openBlade({ name: "McpApprovalDetails", param: item.id });
}

load();
watch([sortExpression, () => pagination.skip], debounce(load, 300));

const bladeToolbar = ref<IBladeToolbar[]>([
  { id: "refresh", title: t("MCP.TOOLBAR.REFRESH"), icon: "lucide-refresh-cw", clickHandler: () => load() },
]);

exposeToChildren({ reload: () => load() });
</script>

<style lang="scss">
.mcp-approvals {
  &__banner {
    margin: 16px;
  }

  &__tool {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }

  &__tool-title {
    font-weight: 600;
  }

  &__tool-name {
    color: var(--neutrals-500);
    font-size: 12px;
  }
}
</style>
