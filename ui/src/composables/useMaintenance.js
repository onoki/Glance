import { computed, ref } from "vue";
import {
  fetchMaintenanceStatus,
  fetchVersion,
  fetchWarnings,
  installUpdate,
  resetRecurrence,
  triggerBackup,
  triggerReindex
} from "../api/maintenance.js";

export const useMaintenance = () => {
  const warnings = ref([]);
  const dismissedWarningIds = ref([]);
  const appVersion = ref("");
  const isBackingUp = ref(false);
  const isReindexing = ref(false);
  const backupStatus = ref("");
  const reindexStatus = ref("");
  const updateStatus = ref("");
  const isUpdating = ref(false);
  const isResettingRecurrence = ref(false);
  const recurrenceStatus = ref("");
  const maintenanceStatus = ref({
    lastBackupAt: null,
    lastBackupError: null,
    lastReindexAt: null,
    recurrenceGeneratedUntil: null
  });

  const visibleWarnings = computed(() =>
    warnings.value.filter((warning) => !dismissedWarningIds.value.includes(warning.id))
  );

  const loadWarnings = async () => {
    try {
      const data = await fetchWarnings();
      warnings.value = data.warnings || [];
    } catch {
      warnings.value = [];
    }
  };

  const loadVersion = async () => {
    try {
      const data = await fetchVersion();
      appVersion.value = data.version || "";
    } catch {
      appVersion.value = "";
    }
  };

  const loadMaintenanceStatus = async () => {
    try {
      const data = await fetchMaintenanceStatus();
      maintenanceStatus.value = {
        lastBackupAt: data.lastBackupAt || null,
        lastBackupError: data.lastBackupError || null,
        lastReindexAt: data.lastReindexAt || null,
        recurrenceGeneratedUntil: data.recurrenceGeneratedUntil || null
      };
    } catch {
      maintenanceStatus.value = {
        lastBackupAt: null,
        lastBackupError: null,
        lastReindexAt: null,
        recurrenceGeneratedUntil: null
      };
    }
  };

  const dismissWarning = (id) => {
    dismissedWarningIds.value = [...dismissedWarningIds.value, id];
  };

  const backupNow = async () => {
    if (isBackingUp.value) {
      return;
    }
    isBackingUp.value = true;
    backupStatus.value = "";
    try {
      await triggerBackup();
      backupStatus.value = "Backup created successfully.";
      await loadWarnings();
      await loadMaintenanceStatus();
    } catch {
      backupStatus.value = "Backup failed. Check the server logs.";
    } finally {
      isBackingUp.value = false;
    }
  };

  const reindexSearch = async () => {
    if (isReindexing.value) {
      return;
    }
    isReindexing.value = true;
    reindexStatus.value = "";
    try {
      await triggerReindex();
      reindexStatus.value = "Search index rebuilt.";
      await loadMaintenanceStatus();
    } catch {
      reindexStatus.value = "Reindex failed. Check the server logs.";
    } finally {
      isReindexing.value = false;
    }
  };

  const applyUpdate = async (file) => {
    if (!file || isUpdating.value) {
      return;
    }
    isUpdating.value = true;
    updateStatus.value = "";
    try {
      const response = await installUpdate(file);
      updateStatus.value = response.message || "Update staged. Restarting...";
    } catch (error) {
      updateStatus.value = error?.message || "Update failed. Check the server logs.";
    } finally {
      isUpdating.value = false;
    }
  };

  const resetRecurrenceGeneration = async () => {
    if (isResettingRecurrence.value) {
      return;
    }
    isResettingRecurrence.value = true;
    recurrenceStatus.value = "";
    try {
      const response = await resetRecurrence();
      const created = Number.isInteger(response?.created) ? response.created : null;
      recurrenceStatus.value = created !== null
        ? `Repeatable tasks regenerated (${created} new).`
        : "Repeatable tasks regenerated.";
      await loadMaintenanceStatus();
    } catch {
      recurrenceStatus.value = "Recurrence reset failed. Check the server logs.";
    } finally {
      isResettingRecurrence.value = false;
    }
  };

  return {
    appVersion,
    backupNow,
    backupStatus,
    dismissWarning,
    applyUpdate,
    isBackingUp,
    isReindexing,
    isUpdating,
    isResettingRecurrence,
    loadMaintenanceStatus,
    loadVersion,
    loadWarnings,
    maintenanceStatus,
    recurrenceStatus,
    reindexSearch,
    reindexStatus,
    resetRecurrenceGeneration,
    updateStatus,
    visibleWarnings
  };
};
