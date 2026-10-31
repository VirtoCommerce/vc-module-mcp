<template>
  <div class="mcp-diff">
    <div
      v-if="preview.object_name || preview.object_id"
      class="mcp-diff__object"
    >
      <span class="mcp-diff__label">{{ $t("MCP.APPROVAL.OBJECT") }}</span>
      <span class="mcp-diff__object-name">{{ preview.object_name || preview.object_id }}</span>
      <span
        v-if="preview.object_type"
        class="mcp-diff__muted"
        >{{ preview.object_type }}</span
      >
    </div>

    <VcBanner
      v-if="preview.warnings?.length"
      variant="warning"
      icon="lucide-triangle-alert"
    >
      <template #title>{{ done ? $t("MCP.APPROVAL.NOTES") : $t("MCP.APPROVAL.WARNINGS") }}</template>
      <ul class="mcp-diff__warnings">
        <li
          v-for="(warning, index) in preview.warnings"
          :key="index"
        >
          {{ warning }}
        </li>
      </ul>
    </VcBanner>

    <!-- a table on desktop; on a phone each change is a card: field, then now -> after -->
    <div
      v-if="preview.changes?.length"
      class="mcp-diff__rows"
      role="table"
    >
      <div
        class="mcp-diff__row mcp-diff__row--head"
        role="row"
      >
        <span role="columnheader">{{ $t("MCP.APPROVAL.FIELD") }}</span>
        <span role="columnheader">{{ $t("MCP.APPROVAL.FROM") }}</span>
        <span role="columnheader">{{ $t("MCP.APPROVAL.TO") }}</span>
      </div>
      <div
        v-for="change in preview.changes"
        :key="change.field"
        class="mcp-diff__row"
        role="row"
      >
        <span
          class="mcp-diff__field"
          role="cell"
          >{{ change.field }}</span
        >
        <span
          class="mcp-diff__from"
          role="cell"
          >{{ formatValue(change.from) }}</span
        >
        <span
          class="mcp-diff__to"
          role="cell"
          >{{ formatValue(change.to) }}</span
        >
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { VcBanner } from "@vc-shell/framework/ui";
import { formatValue, type ChangePreview } from "../utils";

defineProps<{ preview: ChangePreview; done?: boolean }>();
</script>

<style lang="scss">
.mcp-diff {
  display: flex;
  flex-direction: column;
  gap: 12px;

  &__object {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 8px;
  }

  &__label,
  &__muted {
    color: var(--neutrals-500);
    font-size: 13px;
  }

  &__object-name {
    font-weight: 600;
  }

  &__warnings {
    margin: 0;
    padding-left: 18px;
  }

  &__rows {
    display: flex;
    flex-direction: column;
    border: 1px solid var(--neutrals-200);
    border-radius: 6px;
    overflow: hidden;
  }

  &__row {
    display: grid;
    grid-template-columns: minmax(120px, 1fr) minmax(0, 1.5fr) minmax(0, 1.5fr);
    gap: 12px;
    padding: 10px 12px;
    border-top: 1px solid var(--neutrals-200);
    word-break: break-word;

    &:first-child {
      border-top: none;
    }

    &--head {
      background: var(--neutrals-50);
      color: var(--neutrals-500);
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;
    }
  }

  &__field {
    font-weight: 600;
  }

  &__from {
    color: var(--neutrals-500);
    text-decoration: line-through;
  }

  &__to {
    color: var(--success-700, var(--primary-700));
    font-weight: 600;
  }

  @media (max-width: 767px) {
    &__row {
      grid-template-columns: 1fr;
      gap: 4px;

      &--head {
        display: none;
      }
    }

    &__from::before {
      content: "− ";
    }

    &__to::before {
      content: "+ ";
    }
  }
}
</style>
