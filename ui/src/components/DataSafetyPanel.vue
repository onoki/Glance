<template>
  <div class="data-safety-panel">
    <div v-if="startupSafety.recoveryMode" class="recovery-notice" role="alert">
      <strong>Recovery mode</strong>
      <span>{{ startupSafety.message || "Glance has stopped note changes until a verified backup is restored." }}</span>
    </div>
    <div v-if="startupSafety.pendingRestore" class="pending-restore-notice" role="alert">
      <strong>Restore pending</strong>
      <span>
        A verified snapshot is staged. It will replace the current Glance data the next time the app restarts.
        If the automatic restart was cancelled, close and reopen Glance when you are ready.
      </span>
    </div>

    <section class="settings-subsection">
      <h3>Snapshots</h3>
      <p>
        Glance keeps verified local snapshots. By default it retains 48 hourly, 30 daily,
        and 12 monthly restore points; unchanged hours do not create duplicates.
      </p>
      <div class="settings-actions">
        <button class="add-task" :disabled="isWorking" @click="backupNow">
          {{ isCreatingBackup ? "Backing up and verifying..." : "Backup now" }}
        </button>
        <button class="ghost" :disabled="isLoadingDataSafety" @click="loadDataSafety">
          Refresh restore points
        </button>
      </div>
    </section>

    <section class="settings-subsection backup-settings-grid">
      <h3>Backup schedule and second location</h3>
      <label class="settings-checkbox">
        <input v-model="dataSafetySettings.hourlyBackupsEnabled" type="checkbox" />
        Create hourly snapshots while Glance is running and notes have changed
      </label>
      <label class="settings-field">
        <span>Optional second backup location (network drive, USB drive, or another folder)</span>
        <div class="path-input-row">
          <input
            v-model="dataSafetySettings.mirrorDirectory"
            type="text"
            placeholder="For example: P:\Glance backups"
            spellcheck="false"
          />
          <button class="ghost" type="button" @click="pickBackupFolder">Browse...</button>
        </div>
      </label>
      <div class="settings-actions">
        <button class="ghost" :disabled="isTestingBackupLocation" @click="testLocation">
          {{ isTestingBackupLocation ? "Testing..." : "Test location availability and write permissions." }}
        </button>
        <button class="ghost" :disabled="isSavingDataSafety" @click="saveSettings">
          {{ isSavingDataSafety ? "Saving..." : "Save backup settings" }}
        </button>
      </div>
    </section>

    <section class="settings-subsection restore-section">
      <h3>Restore an earlier snapshot</h3>
      <p>
        Choose an actual snapshot below. Restoring replaces notes, people, images, and status JSON
        with that snapshot; Glance first makes an emergency backup of the current state.
      </p>
      <label v-if="backups.length" class="restore-picker">
        Available restore points
        <select v-model="selectedBackupId">
          <option v-for="backup in backups" :key="backup.backupId" :value="backup.backupId">
            {{ formatBackupOption(backup) }}
          </option>
        </select>
      </label>
      <p v-else class="settings-status">
        {{ isLoadingDataSafety ? "Loading restore points..." : "No restore points are available yet." }}
      </p>
      <div v-if="selectedBackup" class="restore-details">
        <span><strong>Verification:</strong> {{ selectedBackup.verificationStatus }}</span>
        <span><strong>Notes:</strong> {{ formatCount(selectedBackup.counts?.tasks) }}</span>
        <span><strong>People:</strong> {{ formatCount(selectedBackup.counts?.people) }}</span>
        <span><strong>Images:</strong> {{ formatCount(selectedBackup.counts?.attachments) }}</span>
        <span><strong>Copies:</strong> {{ backupCopies(selectedBackup) }}</span>
      </div>
      <div class="settings-actions">
        <button class="ghost" :disabled="!selectedBackup || isVerifyingBackup" @click="verifySelectedBackup">
          {{ isVerifyingBackup ? "Verifying..." : "Verify selected restore point" }}
        </button>
        <button class="danger-button" :disabled="!selectedBackup || isRestoringBackup || startupSafety.pendingRestore" @click="restoreBackup">
          {{ startupSafety.pendingRestore ? "Restore already staged" : isRestoringBackup ? "Preparing restore..." : "Restore selected snapshot..." }}
        </button>
      </div>
    </section>

    <section class="settings-subsection portable-export-section">
      <h3>Take your notes with you</h3>
      <p>Download lossless JSON, readable offline HTML, note images, and status JSON in one verified ZIP.</p>
      <button class="ghost" :disabled="isExporting" @click="exportNotes">
        {{ isExporting ? "Preparing export..." : "Export all notes" }}
      </button>
    </section>

    <p v-if="dataSafetyStatus" class="settings-status data-safety-feedback preserve-lines" role="status">
      <strong>Latest result:</strong> {{ dataSafetyStatus }}
    </p>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { exportAllNotes } from "../api/portability.js";
import { useDataSafety } from "../composables/useDataSafety.js";

const props = defineProps({
  onPrepareAction: { type: Function, default: async () => true },
  onPickFolder: { type: Function, default: async () => null },
  onRestartForRestore: { type: Function, default: async () => false }
});

const {
  backups,
  createBackup,
  dataSafetySettings,
  dataSafetyStatus,
  isLoadingDataSafety,
  isRestoringBackup,
  isSavingDataSafety,
  isTestingBackupLocation,
  isVerifyingBackup,
  loadDataSafety,
  restoreSelectedBackup,
  saveSettings,
  selectedBackup,
  selectedBackupId,
  startupSafety,
  testLocation,
  verifySelectedBackup
} = useDataSafety();

const isCreatingBackup = ref(false);
const isExporting = ref(false);
const isWorking = computed(() => isCreatingBackup.value || isRestoringBackup.value || isExporting.value);

onMounted(loadDataSafety);

const prepare = async (reason) => {
  try {
    return (await props.onPrepareAction(reason)) !== false;
  } catch (error) {
    dataSafetyStatus.value = error.message || "Unsaved notes could not be flushed.";
    return false;
  }
};

const backupNow = async () => {
  if (!(await prepare("backup"))) return;
  isCreatingBackup.value = true;
  try {
    await createBackup();
  } catch (error) {
    dataSafetyStatus.value = error.message || "Backup failed.";
  } finally {
    isCreatingBackup.value = false;
  }
};

const pickBackupFolder = async () => {
  try {
    const selected = await props.onPickFolder(dataSafetySettings.value.mirrorDirectory || null);
    if (selected) dataSafetySettings.value.mirrorDirectory = selected;
  } catch (error) {
    dataSafetyStatus.value = error.message || "Unable to choose a folder.";
  }
};

const restoreBackup = async () => {
  if (!selectedBackup.value) return;
  const label = formatBackupOption(selectedBackup.value);
  if (!window.confirm(`Restore this whole Glance snapshot?\n\n${label}\n\nThe current state will first be retained as an emergency backup.`)) return;
  if (!(await prepare("restore"))) return;
  const response = await restoreSelectedBackup();
  if (!response?.ready) return;
  if (response.restartRequired) {
    const restarting = await props.onRestartForRestore();
    if (!restarting) {
      dataSafetyStatus.value = `${response.message} Close and reopen Glance to apply it.`;
    }
  }
};

const exportNotes = async () => {
  if (!(await prepare("export"))) return;
  isExporting.value = true;
  try {
    await exportAllNotes();
    dataSafetyStatus.value = "Portable export downloaded.";
  } catch (error) {
    dataSafetyStatus.value = error.message || "Portable export failed.";
  } finally {
    isExporting.value = false;
  }
};

const formatBackupOption = (backup) => {
  const date = new Date(backup.createdAtUtc);
  const time = Number.isNaN(date.getTime()) ? backup.createdAtLocal : date.toLocaleString();
  const verified = backup.verificationStatus === "verified" ? "verified" : backup.verificationStatus;
  const notes = backup.counts?.tasks == null ? "unknown notes" : `${backup.counts.tasks} notes`;
  return `${time} — ${notes}, ${verified}, ${backup.reason}`;
};

const formatCount = (value) => value == null ? "Unknown" : value.toLocaleString();
const backupCopies = (backup) => [backup.hasLocalCopy ? "local" : null, backup.hasMirrorCopy ? "second location" : null].filter(Boolean).join(" + ") || "Unavailable";
</script>

<style scoped>
.data-safety-panel { display: grid; gap: var(--pad-md); }
.data-safety-panel p, .data-safety-panel h3 { margin: 0; }
.settings-subsection { display: grid; gap: var(--pad-sm); }
.settings-subsection + .settings-subsection { padding-top: var(--pad-md); border-top: 1px solid var(--border-panel); }
.settings-subsection h3 { font-size: var(--font-size-body); line-height: var(--line-height-body); font-weight: 600; }
.settings-subsection > p { color: var(--text-muted); }
.recovery-notice { display: grid; gap: var(--pad-sm); padding: var(--pad-md); border: 1px solid #b42318; background: #fff1f0; color: #7a271a; }
.pending-restore-notice { display: grid; gap: var(--pad-sm); padding: var(--pad-md); border: 1px solid #b54708; background: #fffaeb; color: #7a2e0e; }
.settings-checkbox { display: flex; align-items: center; gap: var(--pad-md); }
.settings-field { display: grid; gap: var(--pad-sm); }
.path-input-row { display: flex; gap: var(--pad-sm); }
.path-input-row input { min-width: 0; flex: 1; }
.restore-picker { display: grid; gap: var(--pad-sm); }
.restore-picker select { width: 100%; min-width: 0; }
.restore-details { display: flex; flex-wrap: wrap; gap: var(--pad-sm) 12px; font-size: var(--font-size-meta); color: var(--text-muted); }
.danger-button { border: 1px solid #b42318; color: #8a1c13; background: transparent; padding: 1px 4px; font-size: var(--font-size-meta); cursor: pointer; }
.danger-button:disabled { opacity: .4; cursor: not-allowed; }
.portable-export-section .ghost { justify-self: start; }
.data-safety-feedback { padding-top: var(--pad-sm); border-top: 1px dotted var(--border-panel); }
.preserve-lines { white-space: pre-line; }
</style>
