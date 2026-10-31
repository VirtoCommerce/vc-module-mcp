<template>
  <VcBlade
    :title="entry.toolName || $t('MCP.AUDIT.DETAILS')"
    :toolbar-items="bladeToolbar"
    width="50%"
  >
    <VcContainer>
      <div class="mcp-audit">
        <VcCard :header="$t('MCP.AUDIT.DETAILS')">
          <div class="mcp-audit__card">
            <VcRow class="mcp-audit__row">
              <VcCol>
                <div class="mcp-audit__status">
                  <VcLabel>{{ $t("MCP.FIELDS.OUTCOME") }}</VcLabel>
                  <VcStatus :variant="outcomeVariant(entry.outcome)">{{ $t(outcomeKey(entry.outcome)) }}</VcStatus>
                </div>
              </VcCol>
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.DATE')"
                  :model-value="formatDateTime(entry.createdDate)"
                />
              </VcCol>
            </VcRow>
            <VcRow class="mcp-audit__row">
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.USER')"
                  :model-value="entry.userName || '—'"
                />
              </VcCol>
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.CLIENT')"
                  :model-value="entry.clientId || '—'"
                />
              </VcCol>
            </VcRow>
            <VcRow class="mcp-audit__row">
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.SCOPE')"
                  :model-value="entry.scopeName"
                />
              </VcCol>
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.DURATION')"
                  :model-value="String(entry.durationMs ?? 0)"
                />
              </VcCol>
            </VcRow>
            <VcField
              v-if="entry.errorMessage"
              class="mcp-audit__error"
              :label="$t('MCP.FIELDS.ERROR')"
              :model-value="entry.errorMessage"
            />
            <div
              v-if="entry.traceId"
              class="mcp-audit__trace"
            >
              <VcLabel>{{ $t("MCP.FIELDS.TRACE_ID") }}</VcLabel>
              <code>{{ entry.traceId }}</code>
              <VcHint>{{ $t("MCP.AUDIT.TRACE_HINT") }}</VcHint>
            </div>
          </div>
        </VcCard>

        <VcCard :header="$t('MCP.APPROVAL.ARGUMENTS')">
          <div class="mcp-audit__card">
            <JsonBlock :json="argumentsJson" />
          </div>
        </VcCard>
      </div>
    </VcContainer>
  </VcBlade>
</template>

<script setup lang="ts">
// One tool call of the audit log. The list passes the row; the change it submitted or executed opens from here.
import { useBlade, usePermissions, type IBladeToolbar } from "@vc-shell/framework";
import { VcBlade, VcCard, VcCol, VcContainer, VcField, VcHint, VcLabel, VcRow, VcStatus } from "@vc-shell/framework/ui";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { McpAuditEntry } from "../api";
import JsonBlock from "../components/JsonBlock.vue";
import { formatDateTime, outcomeKey, outcomeVariant, parseJson } from "../utils";

defineBlade({
  url: "/mcp-audit-entry",
  name: "McpAuditDetails",
  permissions: ["mcp:audit:read"],
  routable: false,
});

const { t } = useI18n({ useScope: "global" });
const { options, openBlade } = useBlade<{ entry?: McpAuditEntry }>();
const { hasAccess } = usePermissions();

const entry = computed<McpAuditEntry>(() => options.value?.entry ?? ({ id: "" } as McpAuditEntry));
const argumentsJson = computed(() => {
  const value = parseJson<unknown>(entry.value.arguments);
  return value === undefined ? entry.value.arguments : JSON.stringify(value, null, 2);
});

const bladeToolbar = ref<IBladeToolbar[]>([
  {
    id: "openChange",
    title: t("MCP.AUDIT.OPEN_CHANGE"),
    icon: "lucide-shield-check",
    isVisible: computed(() => !!entry.value.pendingChangeId),
    disabled: computed(() => !hasAccess("mcp:approve")),
    clickHandler: () => openBlade({ name: "McpApprovalDetails", param: entry.value.pendingChangeId }),
  },
]);
</script>

<style lang="scss">
.mcp-audit {
  padding: 20px 22px 36px;
  display: flex;
  flex-direction: column;
  gap: 16px;

  &__card {
    display: flex;
    flex-direction: column;
    gap: 16px;
    padding: 16px;
  }

  &__row {
    gap: 16px;
  }

  &__status,
  &__trace {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 4px;
  }

  &__trace code {
    font-size: 13px;
    word-break: break-all;
  }

  &__error .vc-field__text {
    white-space: pre-wrap;
    color: var(--danger-600, var(--danger-500));
  }

  @media (max-width: 767px) {
    padding: 12px;

    &__row {
      flex-direction: column;
    }
  }
}
</style>
