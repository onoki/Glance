import { formatDateKey, getWeekStart, parseDateKey, toWeekdayNumber, weekdayLabels } from "./dateUtils.js";

export const groupTasksByWeekday = (tasks) => {
  const groups = new Map();
  tasks.forEach((task) => {
    const date = task.scheduledDate ? parseDateKey(task.scheduledDate) : new Date();
    const weekday = toWeekdayNumber(date || new Date());
    if (!groups.has(weekday)) {
      groups.set(weekday, []);
    }
    groups.get(weekday).push(task);
  });

  return weekdayLabels
    .map((label, index) => {
      const day = index + 1;
      return { id: `weekday-${day}`, label, tasks: groups.get(day) || [] };
    })
    .filter((group) => group.tasks.length > 0);
};

export const isThisWeekCategory = (category) => category.label === "This week";

export const deriveCategories = (tasks) => {
  const now = new Date();
  now.setHours(0, 0, 0, 0);
  const currentWeekStart = getWeekStart(now);
  const futureLimit = new Date(currentWeekStart);
  futureLimit.setDate(currentWeekStart.getDate() + 28);

  const repeatable = [];
  const uncategorized = [];
  const notes = [];
  const noDate = [];
  const weekGroups = new Map();

  tasks.forEach((task) => {
    if (task.recurrence) {
      if (task.recurrence.type === "notes") {
        notes.push(task);
        return;
      }
      repeatable.push(task);
      return;
    }

    if (task.scheduledDate) {
      const scheduled = parseDateKey(task.scheduledDate);
      if (!scheduled) {
        noDate.push(task);
        return;
      }
      const weekStart = getWeekStart(scheduled);
      if (weekStart > futureLimit) {
        return;
      }
      const weekKey = formatDateKey(weekStart);
      if (!weekGroups.has(weekKey)) {
        weekGroups.set(weekKey, []);
      }
      weekGroups.get(weekKey).push(task);
      return;
    }

    uncategorized.push(task);
  });

  const categories = [];
  if (uncategorized.length) {
    categories.push({ id: "uncategorized", label: "Uncategorized", tasks: uncategorized });
  }

  const weekKeys = Array.from(weekGroups.keys()).sort();
  const pastWeeks = [];
  const futureWeeks = [];
  const nextWeek = new Date(currentWeekStart);
  nextWeek.setDate(currentWeekStart.getDate() + 7);
  const thisWeekKey = formatDateKey(currentWeekStart);
  const nextWeekKey = formatDateKey(nextWeek);

  weekKeys.forEach((key) => {
    const weekDate = parseDateKey(key);
    if (!weekDate) {
      return;
    }
    if (weekDate < currentWeekStart) {
      pastWeeks.push(key);
      return;
    }
    if (key === thisWeekKey || key === nextWeekKey) {
      return;
    }
    futureWeeks.push(key);
  });

  pastWeeks.forEach((key) => {
    categories.push({ id: `week-${key}`, label: `Week starting ${key}`, tasks: weekGroups.get(key) });
  });

  if (weekGroups.has(thisWeekKey)) {
    categories.push({ id: `week-${thisWeekKey}`, label: "This week", tasks: weekGroups.get(thisWeekKey) });
  }

  if (weekGroups.has(nextWeekKey)) {
    categories.push({ id: `week-${nextWeekKey}`, label: "Next week", tasks: weekGroups.get(nextWeekKey) });
  }

  futureWeeks.forEach((key) => {
    categories.push({ id: `week-${key}`, label: `Week starting ${key}`, tasks: weekGroups.get(key) });
  });

  if (noDate.length) {
    categories.push({ id: "no-date", label: "No date", tasks: noDate });
  }

  if (repeatable.length) {
    categories.push({ id: "repeatable", label: "Repeatable", tasks: repeatable });
  }

  if (notes.length) {
    categories.push({ id: "notes", label: "Notes", tasks: notes });
  }

  return categories;
};
