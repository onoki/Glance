<template>
  <section class="settings-view">
    <div class="settings-card">
      <h2>Data safety</h2>
      <p>Back up your data and manage maintenance tasks.</p>
      <div class="settings-actions">
        <button class="add-task" :disabled="isBackingUp" @click="onBackupNow">
          {{ isBackingUp ? "Backing up..." : "Backup now" }}
        </button>
        <button class="ghost" :disabled="isReindexing" @click="onReindexSearch">
          {{ isReindexing ? "Reindexing..." : "Reindex search" }}
        </button>
      </div>
      <div class="settings-status-list">
        <div><strong>Last backup:</strong> {{ maintenanceStatus.lastBackupAt || "Never" }}</div>
        <div v-if="maintenanceStatus.lastBackupError">
          <strong>Last backup error:</strong> {{ maintenanceStatus.lastBackupError }}
        </div>
        <div><strong>Last reindex:</strong> {{ maintenanceStatus.lastReindexAt || "Never" }}</div>
      </div>
      <p v-if="backupStatus" class="settings-status">{{ backupStatus }}</p>
      <p v-if="reindexStatus" class="settings-status">{{ reindexStatus }}</p>
    </div>
    <div class="settings-card">
      <h2>About</h2>
      <div class="settings-status-list">
        <div><strong>Version:</strong> {{ appVersion || "Unknown" }} UTC</div>
      </div>
    </div>
    <div class="settings-card">
      <h2>Licenses</h2>
      <p class="settings-status">
        Fontpkg-PxPlus_IBM_VGA8 by pocketfood (CC BY-SA 4.0):
        https://github.com/pocketfood/Fontpkg-PxPlus_IBM_VGA8
      </p>
    </div>
  </section>
</template>

<script setup>
defineProps({
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
  onBackupNow: {
    type: Function,
    required: true
  },
  onReindexSearch: {
    type: Function,
    required: true
  }
});
</script>
