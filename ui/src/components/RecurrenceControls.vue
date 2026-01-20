<template>
  <div class="recurrence-row">
    <label class="recurrence-label">Frequency</label>
    <select :value="recurrenceType" class="recurrence-select" @change="handleRecurrenceChange">
      <option value="weekly">Weekly</option>
      <option value="monthly">Monthly</option>
    </select>
    <div v-if="recurrenceType === 'weekly'" class="weekday-row">
      <button
        v-for="day in weekdayOptions"
        :key="day.value"
        type="button"
        class="weekday-chip"
        :class="{ active: weeklyDays.includes(day.value) }"
        @click="$emit('toggle-weekday', day.value)"
      >
        {{ day.label }}
      </button>
    </div>
    <div v-if="recurrenceType === 'monthly'" class="monthday-row">
      <label class="recurrence-label">Days</label>
      <input
        :value="monthDaysInput"
        class="monthday-input"
        type="text"
        placeholder="1, 15, 30"
        @input="handleMonthDaysInput"
        @blur="$emit('apply')"
      />
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  recurrenceType: {
    type: String,
    default: ""
  },
  weeklyDays: {
    type: Array,
    default: () => []
  },
  monthDaysInput: {
    type: String,
    default: ""
  },
  weekdayOptions: {
    type: Array,
    default: () => []
  }
});

const emit = defineEmits(["update:recurrenceType", "update:monthDaysInput", "toggle-weekday", "apply"]);

const handleRecurrenceChange = (event) => {
  emit("update:recurrenceType", event.target.value);
  emit("apply");
};

const handleMonthDaysInput = (event) => {
  emit("update:monthDaysInput", event.target.value);
};
</script>

<style scoped>
.recurrence-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.recurrence-label {
  font-weight: 600;
}

.recurrence-select {
  border: 1px solid var(--border-panel);
  border-radius: 0;
  padding: 2px 8px;
  background: var(--bg-panel);
  font-size: 0.75rem;
}

.weekday-row {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.weekday-chip {
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-main);
  padding: 2px 6px;
  border-radius: 0;
  font-size: 0.7rem;
  cursor: pointer;
}

.weekday-chip.active {
  background: var(--text-main);
  color: var(--text-invert);
  border-color: var(--text-main);
}

.monthday-row {
  display: flex;
  gap: 6px;
  align-items: center;
}

.monthday-input {
  border: 1px solid var(--border-panel);
  border-radius: 0;
  padding: 2px 8px;
  background: var(--bg-panel);
  font-size: 0.75rem;
  width: 120px;
}
</style>
