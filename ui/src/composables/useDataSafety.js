import { computed, ref } from "vue";
import {
  createVerifiedBackup,
  fetchBackupCatalog,
  fetchDataSafetySettings,
  fetchStartupSafety,
  saveDataSafetySettings,
  stageBackupRestore,
  testBackupLocation,
  verifyBackup
} from "../api/dataSafety.js";

const defaultSettings = () => ({
  hourlyBackupsEnabled: true,
  mirrorDirectory: "",
  hourlyRetentionCount: 48,
  dailyRetentionDays: 30,
  monthlyRetentionMonths: 12
});

export const useDataSafety = () => {
  const backups = ref([]);
  const dataSafetySettings = ref(defaultSettings());
  const startupSafety = ref({ healthy: true, recoveryMode: false, pendingRestore: false, message: "" });
  const selectedBackupId = ref("");
  const dataSafetyStatus = ref("");
  const isLoadingDataSafety = ref(false);
  const isSavingDataSafety = ref(false);
  const isTestingBackupLocation = ref(false);
  const isVerifyingBackup = ref(false);
  const isRestoringBackup = ref(false);

  const selectedBackup = computed(() =>
    backups.value.find((backup) => backup.backupId === selectedBackupId.value) || null
  );

  const loadBackupCatalog = async () => {
    const response = await fetchBackupCatalog();
    backups.value = [...(response.backups || [])].sort(
      (left, right) => new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime()
    );
    if (!backups.value.some((backup) => backup.backupId === selectedBackupId.value)) {
      selectedBackupId.value = backups.value.find((backup) => backup.verificationStatus === "verified")?.backupId
        || backups.value[0]?.backupId
        || "";
    }
  };

  const loadDataSafety = async () => {
    isLoadingDataSafety.value = true;
    try {
      const [settings, startup] = await Promise.all([
        fetchDataSafetySettings(),
        fetchStartupSafety()
      ]);
      dataSafetySettings.value = { ...defaultSettings(), ...settings, mirrorDirectory: settings.mirrorDirectory || "" };
      startupSafety.value = startup;
      await loadBackupCatalog();
    } catch (error) {
      dataSafetyStatus.value = error.message || "Unable to load data-safety settings.";
    } finally {
      isLoadingDataSafety.value = false;
    }
  };

  const createBackup = async () => {
    dataSafetyStatus.value = "";
    const response = await createVerifiedBackup();
    dataSafetyStatus.value = response.mirrorError
      ? `Local backup verified. Mirror copy failed: ${response.mirrorError}`
      : "Backup created and verified.";
    await loadBackupCatalog();
    if (response.backupId) selectedBackupId.value = response.backupId;
    return response;
  };

  const saveSettings = async () => {
    isSavingDataSafety.value = true;
    dataSafetyStatus.value = "";
    try {
      const saved = await saveDataSafetySettings({
        ...dataSafetySettings.value,
        mirrorDirectory: dataSafetySettings.value.mirrorDirectory?.trim() || null
      });
      dataSafetySettings.value = { ...defaultSettings(), ...saved, mirrorDirectory: saved.mirrorDirectory || "" };
      dataSafetyStatus.value = "Backup settings saved.";
      return true;
    } catch (error) {
      dataSafetyStatus.value = error.message || "Unable to save backup settings.";
      return false;
    } finally {
      isSavingDataSafety.value = false;
    }
  };

  const testLocation = async () => {
    isTestingBackupLocation.value = true;
    dataSafetyStatus.value = "";
    try {
      const result = await testBackupLocation(dataSafetySettings.value.mirrorDirectory?.trim() || null);
      dataSafetyStatus.value = result.message;
      return result.available && result.writable;
    } catch (error) {
      dataSafetyStatus.value = error.message || "Unable to test the backup location.";
      return false;
    } finally {
      isTestingBackupLocation.value = false;
    }
  };

  const verifySelectedBackup = async () => {
    if (!selectedBackupId.value) return false;
    isVerifyingBackup.value = true;
    dataSafetyStatus.value = "";
    try {
      const result = await verifyBackup(selectedBackupId.value);
      dataSafetyStatus.value = result.isValid ? "The selected restore point passed verification." : result.errors?.join("\n");
      await loadBackupCatalog();
      return !!result.isValid;
    } catch (error) {
      dataSafetyStatus.value = error.message || "Backup verification failed.";
      await loadBackupCatalog();
      return false;
    } finally {
      isVerifyingBackup.value = false;
    }
  };

  const restoreSelectedBackup = async () => {
    if (!selectedBackupId.value) return null;
    isRestoringBackup.value = true;
    dataSafetyStatus.value = "";
    try {
      const response = await stageBackupRestore(selectedBackupId.value);
      dataSafetyStatus.value = response.message;
      if (response.ready) {
        startupSafety.value = { ...startupSafety.value, pendingRestore: true };
      }
      return response;
    } catch (error) {
      dataSafetyStatus.value = error.message || "Unable to prepare the restore.";
      return null;
    } finally {
      isRestoringBackup.value = false;
    }
  };

  return {
    backups,
    createBackup,
    dataSafetySettings,
    dataSafetyStatus,
    isLoadingDataSafety,
    isRestoringBackup,
    isSavingDataSafety,
    isTestingBackupLocation,
    isVerifyingBackup,
    loadBackupCatalog,
    loadDataSafety,
    restoreSelectedBackup,
    saveSettings,
    selectedBackup,
    selectedBackupId,
    startupSafety,
    testLocation,
    verifySelectedBackup
  };
};
