<template>
  <pre
    v-if="json"
    class="mcp-json"
    >{{ json }}</pre
  >
  <span
    v-else
    class="mcp-json__empty"
    >—</span
  >
</template>

<script setup lang="ts">
defineProps<{ json?: string }>();
</script>

<style lang="scss">
.mcp-json {
  margin: 0;
  padding: 12px;
  max-height: 360px;
  overflow: auto;
  border-radius: 6px;
  background: var(--neutrals-50);
  border: 1px solid var(--neutrals-200);
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;

  &__empty {
    color: var(--neutrals-500);
  }
}
</style>
