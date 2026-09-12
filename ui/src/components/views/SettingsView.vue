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
      <h2>Search maintenance</h2>
      <div class="settings-card-content">
        <p class="settings-description">The search index is derived from your notes and can be safely rebuilt.</p>
        <div class="settings-actions">
          <button class="ghost" :disabled="isReindexing" @click="onReindexSearch">
            {{ isReindexing ? "Reindexing..." : "Reindex search" }}
          </button>
        </div>
        <div class="settings-status-list">
          <div><strong>Last reindex:</strong> {{ maintenanceStatus.lastReindexAt || "Never" }}</div>
        </div>
        <p v-if="reindexStatus" class="settings-status">{{ reindexStatus }}</p>
      </div>
    </section>
    <section class="settings-card">
      <h2>Repeatables</h2>
      <div class="settings-card-content">
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
        <p v-if="recurrenceStatus" class="settings-status">{{ recurrenceStatus }}</p>
      </div>
    </section>
    <section class="settings-card">
      <h2>About</h2>
      <div class="settings-card-content">
        <div class="settings-status-list">
          <div><strong>Version:</strong> {{ appVersion || "Unknown" }} UTC</div>
        </div>
      </div>
    </section>
    <section class="settings-card">
      <h2>App updates</h2>
      <div class="settings-card-content">
        <p class="settings-description">Install a Glance update package.</p>
        <div class="settings-actions">
          <button class="add-task" :disabled="isUpdating" @click="pickUpdate">
            {{ isUpdating ? "Updating..." : "Install update..." }}
          </button>
          <input
            ref="updateInput"
            type="file"
            accept=".zip"
            style="display: none"
            @change="handleUpdateFile"
          />
        </div>
        <p v-if="updateStatus" class="settings-status">{{ updateStatus }}</p>
      </div>
    </section>
    <section class="settings-card">
      <h2>Licenses</h2>
      <div class="settings-card-content">
        <p class="settings-status">
          Fontpkg-PxPlus_IBM_VGA8 by pocketfood (CC BY-SA 4.0):
          https://github.com/pocketfood/Fontpkg-PxPlus_IBM_VGA8
        </p>
      </div>
    </section>
  </section>
</template>

<script setup>
import { ref } from "vue";
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
  isUpdating: {
    type: Boolean,
    required: true
  },
  updateStatus: {
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
  onApplyUpdate: {
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

const updateInput = ref(null);

const handleResetRecurrence = () => {
  const confirmed = window.confirm("Reset repeatables and regenerate starting today?");
  if (!confirmed) {
    return;
  }
  props.onResetRecurrence();
};

const pickUpdate = () => {
  updateInput.value?.click();
};

const handleUpdateFile = async (event) => {
  const file = event.target?.files?.[0];
  event.target.value = "";
  if (!file) {
    return;
  }
  await props.onApplyUpdate(file);
};
</script>
