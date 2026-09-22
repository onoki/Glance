<template>
  <section class="settings-view">
    <section class="settings-card">
      <h2>Data safety</h2>
      <div class="settings-card-content">
        <DataSafetyPanel
          :on-prepare-action="onPrepareDataAction"
          :on-pick-folder="onPickBackupFolder"
          :on-restart-for-restore="onRestartForRestore"
        />
      </div>
    </section>
    <section class="settings-card">
      <h2>Maintenance</h2>
      <div class="settings-card-content">
        <section class="settings-subsection">
          <h3>Search</h3>
          <p class="settings-description">The search index is derived from your notes and can be safely rebuilt.</p>
          <div class="settings-actions">
            <button class="ghost" :disabled="isReindexing" @click="onReindexSearch">
              {{ isReindexing ? "Reindexing..." : "Reindex search" }}
            </button>
          </div>
          <div class="settings-status-list">
            <div><strong>Last reindex:</strong> {{ maintenanceStatus.lastReindexAt || "Never" }}</div>
          </div>
          <p v-if="reindexStatus" class="settings-status" role="status">{{ reindexStatus }}</p>
        </section>
        <section class="settings-subsection">
          <h3>Repeatable tasks</h3>
          <p class="settings-description">Reset repeatable generation and recreate items starting today.</p>
          <div class="settings-actions">
            <button class="ghost" :disabled="isResettingRecurrence" @click="handleResetRecurrence">
              {{ isResettingRecurrence ? "Resetting..." : "Reset repeatables" }}
            </button>
          </div>
          <div class="settings-status-list">
            <div>
              <strong>Generated until:</strong>
              {{ maintenanceStatus.recurrenceGeneratedUntil || "Unknown" }}
            </div>
          </div>
          <p v-if="recurrenceStatus" class="settings-status" role="status">{{ recurrenceStatus }}</p>
        </section>
      </div>
    </section>
    <section class="settings-card">
      <h2>About</h2>
      <div class="settings-card-content">
        <div class="settings-status-list">
          <div><strong>Version:</strong> {{ appVersion || "Unknown" }} UTC</div>
        </div>
        <section class="settings-subsection">
          <h3>Font licenses</h3>
          <p class="settings-status">BigBlue TerminalPlus by VileR (CC BY-SA 4.0), used unmodified.</p>
        </section>
      </div>
    </section>
  </section>
</template>

<script setup>
import DataSafetyPanel from "../DataSafetyPanel.vue";
const props = defineProps({
  isBackingUp: {
    type: Boolean,
    required: true
  },
  isReindexing: {
    type: Boolean,
    required: true
  },
  backupStatus: {
    type: String,
    required: true
  },
  reindexStatus: {
    type: String,
    required: true
  },
  maintenanceStatus: {
    type: Object,
    required: true
  },
  appVersion: {
    type: String,
    required: true
  },
  isResettingRecurrence: {
    type: Boolean,
    required: true
  },
  recurrenceStatus: {
    type: String,
    required: true
  },
  onBackupNow: {
    type: Function,
    required: true
  },
  onReindexSearch: {
    type: Function,
    required: true
  },
  onResetRecurrence: {
    type: Function,
    required: true
  },
  onPrepareDataAction: {
    type: Function,
    default: async () => true
  },
  onPickBackupFolder: {
    type: Function,
    default: async () => null
  },
  onRestartForRestore: {
    type: Function,
    default: async () => false
  }
});


const handleResetRecurrence = () => {
  const confirmed = window.confirm("Reset repeatables and regenerate starting today?");
  if (!confirmed) {
    return;
  }
  props.onResetRecurrence();
};

</script>
