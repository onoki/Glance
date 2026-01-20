import { computed, ref } from "vue";
import { fetchHistory } from "../api/history.js";
import { formatDateKey } from "../utils/dateUtils.js";
import { normalizeTask } from "../utils/taskUtils.js";

const buildHistorySeries = (stats) => {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const start = new Date(today);
  start.setDate(today.getDate() - 179);

  const map = new Map(stats.map((item) => [item.date, item.count]));
  const series = [];
  for (let i = 0; i < 180; i += 1) {
    const day = new Date(start);
    day.setDate(start.getDate() + i);
    const dateKey = formatDateKey(day);
    series.push({ date: dateKey, count: map.get(dateKey) || 0 });
  }
  return series;
};

export const useHistory = () => {
  const historyGroups = ref([]);
  const historyStats = ref([]);

  const historySeries = computed(() => buildHistorySeries(historyStats.value || []));

  const historyMax = computed(() =>
    historySeries.value.reduce((max, item) => Math.max(max, item.count), 0)
  );

  const historyBars = computed(() => {
    const maxCount = historyMax.value || 1;
    return historySeries.value.map((item) => ({
      ...item,
      height: maxCount === 0 ? 0 : (item.count / maxCount) * 100
    }));
  });

  const historyScale = computed(() => {
    const maxCount = historyMax.value;
    if (maxCount === 0) {
      return [{ label: "0" }];
    }
    const mid = Math.floor(maxCount / 2);
    const ticks = [maxCount];
    if (mid > 0 && mid !== maxCount) {
      ticks.push(mid);
    }
    ticks.push(0);
    return ticks.map((value) => ({ label: `${value}` }));
  });

  const loadHistory = async () => {
    const data = await fetchHistory();
    historyStats.value = data.stats || [];
    historyGroups.value = (data.groups || []).map((group) => ({
      date: group.date,
      tasks: group.tasks.map(normalizeTask)
    }));
  };

  return {
    historyBars,
    historyGroups,
    historyScale,
    historySeries,
    loadHistory
  };
};
