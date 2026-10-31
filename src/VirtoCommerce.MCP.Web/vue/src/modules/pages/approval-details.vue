<template>
  <VcBlade
    :title="title"
    :toolbar-items="bladeToolbar"
    :loading="loading"
    width="50%"
  >
    <VcContainer>
      <div
        v-if="details"
        class="mcp-approval"
      >
        <VcBanner
          v-if="details.canReview && details.cannotApproveReason"
          variant="info"
          icon="lucide-info"
        >
          {{ details.cannotApproveReason }}
        </VcBanner>

        <VcCard :header="isDone ? $t('MCP.APPROVAL.WHAT_CHANGED') : $t('MCP.APPROVAL.WHAT_CHANGES')">
          <div class="mcp-approval__card">
            <ChangeDiff
              v-if="diff"
              :preview="diff"
              :done="isDone"
            />
            <template v-else>
              <p class="mcp-approval__muted">{{ $t("MCP.APPROVAL.NO_DIFF") }}</p>
              <JsonBlock :json="isDone ? details.resultJson : details.previewJson" />
            </template>
          </div>
        </VcCard>

        <VcCard
          v-if="details.canReview"
          :header="$t('MCP.APPROVAL.REVIEW')"
        >
          <div class="mcp-approval__card">
            <VcTextarea
              v-model="comment"
              :label="$t('MCP.APPROVAL.COMMENT')"
              :placeholder="$t('MCP.APPROVAL.COMMENT_PLACEHOLDER')"
              maxlength="1024"
            />
          </div>
        </VcCard>

        <VcCard :header="$t('MCP.APPROVAL.REQUEST')">
          <div class="mcp-approval__card">
            <VcRow class="mcp-approval__row">
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.TOOL')"
                  :model-value="change.toolName"
                />
              </VcCol>
              <VcCol>
                <div class="mcp-approval__status">
                  <VcLabel>{{ $t("MCP.FIELDS.STATUS") }}</VcLabel>
                  <VcStatus :variant="changeStatusVariant(details.status)">{{ $t(changeStatusKey(details.status)) }}</VcStatus>
                </div>
              </VcCol>
            </VcRow>
            <VcRow class="mcp-approval__row">
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.REQUESTED_BY')"
                  :model-value="change.requestedByUserName || '—'"
                />
              </VcCol>
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.SCOPE')"
                  :model-value="change.scopeName"
                />
              </VcCol>
            </VcRow>
            <VcRow class="mcp-approval__row">
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.REQUESTED_AT')"
                  :model-value="change.createdDate"
                  type="date-ago"
                />
              </VcCol>
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.EXPIRES')"
                  :model-value="formatDateTime(change.expirationDate)"
                />
              </VcCol>
            </VcRow>
            <VcRow
              v-if="change.reviewedByUserName"
              class="mcp-approval__row"
            >
              <VcCol>
                <VcField
                  :label="$t('MCP.FIELDS.REVIEWED_BY')"
                  :model-value="change.reviewedByUserName"
                />
              </VcCol>
              <VcCol>
                <VcField
                  :label="$t('MCP.APPROVAL.COMMENT')"
                  :model-value="change.reviewComment || '—'"
                />
              </VcCol>
            </VcRow>
          </div>
        </VcCard>

        <VcCard
          v-if="details.resultJson && diff !== resultDiff"
          :header="$t('MCP.APPROVAL.RESULT')"
        >
          <div class="mcp-approval__card">
            <JsonBlock :json="details.resultJson" />
          </div>
        </VcCard>

        <VcCard :header="$t('MCP.APPROVAL.ARGUMENTS')">
          <div class="mcp-approval__card">
            <JsonBlock :json="details.argumentsJson" />
          </div>
        </VcCard>
      </div>
    </VcContainer>
  </VcBlade>
</template>

<script setup lang="ts">
// A change an agent asked to make: the diff of its dry run, the request, and the reviewer's decision. Approving runs
// the tool right away as the reviewer; the platform refuses a reviewer who requested the change.
import { notification, useAsync, useBlade, usePopup, type IBladeToolbar } from "@vc-shell/framework";
import {
  VcBanner,
  VcBlade,
  VcCard,
  VcCol,
  VcContainer,
  VcField,
  VcLabel,
  VcRow,
  VcStatus,
  VcTextarea,
} from "@vc-shell/framework/ui";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { mcpApi, type McpPendingChange, type McpPendingChangeDetails } from "../api";
import ChangeDiff from "../components/ChangeDiff.vue";
import JsonBlock from "../components/JsonBlock.vue";
import { asChangePreview, changeStatusKey, changeStatusVariant, formatDateTime, humanize } from "../utils";

defineBlade({
  url: "/mcp-approval",
  name: "McpApprovalDetails",
});

const { t } = useI18n({ useScope: "global" });
const { param, callParent } = useBlade();
const { showConfirmation } = usePopup();

const details = ref<McpPendingChangeDetails>();
const comment = ref("");

const change = computed<McpPendingChange>(() => details.value?.change ?? ({ id: "" } as McpPendingChange));
const isDone = computed(() => details.value?.status === "Applied" || details.value?.status === "Failed");
const previewDiff = computed(() => asChangePreview(details.value?.previewJson));
const resultDiff = computed(() => asChangePreview(details.value?.resultJson));
// once applied, the tool's own result shows what changed; before, the dry run shows what would
const diff = computed(() => (isDone.value ? (resultDiff.value ?? previewDiff.value) : previewDiff.value));
const title = computed(() => humanize(change.value.toolName) || t("MCP.APPROVAL.TITLE"));

const { loading, action: load } = useAsync(async () => {
  try {
    details.value = await mcpApi.getPendingChange(param.value!);
  } catch (error) {
    notification.error(t("MCP.ALERTS.LOAD_FAILED", { message: (error as Error).message }));
  }
});

const { loading: reviewing, action: review } = useAsync(async (decide?: () => Promise<McpPendingChangeDetails>) => {
  try {
    details.value = await decide!();
    comment.value = "";
    if (details.value.status === "Rejected") notification.success(t("MCP.APPROVAL.REJECTED"));
    else if (details.value.status === "Failed") notification.error(t("MCP.APPROVAL.APPROVED_FAILED"));
    else notification.success(t("MCP.APPROVAL.APPROVED"));
    callParent("reload");
  } catch (error) {
    notification.error((error as Error).message);
    await load();
  }
});

const bladeToolbar = ref<IBladeToolbar[]>([
  {
    id: "approve",
    title: t("MCP.TOOLBAR.APPROVE"),
    icon: "lucide-check",
    disabled: computed(() => reviewing.value || !details.value?.canReview || !!details.value?.cannotApproveReason),
    async clickHandler() {
      if (await showConfirmation(t("MCP.ALERTS.APPROVE")))
        await review(() => mcpApi.approve(param.value!, comment.value || undefined));
    },
  },
  {
    id: "reject",
    title: t("MCP.TOOLBAR.REJECT"),
    icon: "lucide-ban",
    disabled: computed(() => reviewing.value || !details.value?.canReview),
    async clickHandler() {
      if (await showConfirmation(t("MCP.ALERTS.REJECT")))
        await review(() => mcpApi.reject(param.value!, comment.value || undefined));
    },
  },
  { id: "refresh", title: t("MCP.TOOLBAR.REFRESH"), icon: "lucide-refresh-cw", clickHandler: () => load() },
]);

load();
</script>

<style lang="scss">
.mcp-approval {
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

  &__status {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 4px;
  }

  &__muted {
    margin: 0;
    color: var(--neutrals-500);
  }

  @media (max-width: 767px) {
    padding: 12px;

    &__row {
      flex-direction: column;
    }
  }
}
</style>
