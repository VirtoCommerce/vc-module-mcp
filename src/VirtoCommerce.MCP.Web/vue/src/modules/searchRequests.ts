// What the Admin Shell's global search asks a list to show: the list takes the keyword when it opens, or at once when
// it is already open, and clears the request.
import { ref } from "vue";

export const requestedToolSearch = ref<string>();
export const requestedAgentSearch = ref<string>();
