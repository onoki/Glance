import { shallowRef } from "vue";

// Anchor hover to a member of the visible run, so separate lists stay independent.
export const hoveredGroupTaskId = shallowRef(null);
