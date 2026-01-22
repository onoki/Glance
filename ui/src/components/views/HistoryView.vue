<template>
  <section class="history-view">
    <div class="history-toolbar">
      <button class="ghost" @click="onMoveCompletedToHistory">Move completed to history</button>
    </div>
    <div class="history-chart">
      <div class="chart-area">
        <div class="chart-bars">
          <div
            v-for="day in historyBars"
            :key="day.date"
            class="chart-bar"
            :style="{ height: `${day.height}%` }"
            :title="`${day.date}: ${day.count}`"
          ></div>
        </div>
        <div class="chart-scale">
          <span v-for="tick in historyScale" :key="tick.label">{{ tick.label }}</span>
        </div>
      </div>
      <div class="chart-axis">
        <span>{{ historySeries[0]?.date }}</span>
        <span>{{ historySeries[historySeries.length - 1]?.date }}</span>
      </div>
    </div>
    <div class="history-list">
      <div v-if="historyGroups.length === 0" class="history-empty">No completed tasks yet.</div>
      <div v-else>
        <section v-for="group in historyGroups" :key="group.date" class="history-group">
          <h3 class="history-date">{{ group.date }}</h3>
          <div class="task-list">
            <TaskItem
              v-for="task in group.tasks"
              :key="task.id"
              :task="task"
              :read-only="true"
              :allow-toggle="true"
              :focus-title-id="null"
              :focus-content-target="null"
              :on-save="noop"
              :on-complete="onComplete"
              :on-dirty="noop"
              :on-create-below="noop"
              :on-tab-to-previous="noopAsync"
              :on-split-to-new-task="noop"
              :on-focus-prev-task-from-title="noop"
              :on-focus-next-task-from-content="noop"
              :on-delete="noop"
            />
          </div>
        </section>
      </div>
    </div>
  </section>
</template>

<script setup>
import TaskItem from "../TaskItem.vue";

defineProps({
  historyBars: {
    type: Array,
    required: true
  },
  historyScale: {
    type: Array,
    required: true
  },
  historySeries: {
    type: Array,
    required: true
  },
  historyGroups: {
    type: Array,
    required: true
  },
  onMoveCompletedToHistory: {
    type: Function,
    required: true
  },
  onComplete: {
    type: Function,
    required: true
  },
  noop: {
    type: Function,
    required: true
  },
  noopAsync: {
    type: Function,
    required: true
  }
});
</script>
