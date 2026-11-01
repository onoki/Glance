import { formatDateKey, getWeekStart, parseDateKey, toWeekdayNumber, weekdayLabels, weekdayNames } from "./dateUtils.js";

export const weekDays = (today = new Date(), weekStart = getWeekStart(today)) => {
  const start = getWeekStart(weekStart);
  const todayKey = formatDateKey(today);
  return weekdayLabels.map((label, index) => {
    const date = new Date(start);
    date.setDate(start.getDate() + index);
    const dateKey = formatDateKey(date);
    return { id: `weekday-${index + 1}`, label, name: weekdayNames[index], dateKey, isPast: dateKey < todayKey };
  });
};

export const currentWeekDays = (today = new Date()) => weekDays(today);
export const relativeWeekStart = (weeks, today = new Date()) => {
  const start = getWeekStart(today);
  start.setDate(start.getDate() + weeks * 7);
  return start;
};
export const nextWeekDays = (today = new Date()) => weekDays(today, relativeWeekStart(1, today));
export const planningWeekDays = (today = new Date()) => [...currentWeekDays(today), ...nextWeekDays(today)];
export const categoryPickerDays = (id, today = new Date()) => id === 'this-week' ? currentWeekDays(today) : id === 'next-week' ? nextWeekDays(today) : [];

export const groupTasksByWeekday = (tasks, today = new Date(), weekStart = getWeekStart(today)) => {
  const groups = new Map();
  tasks.forEach((task) => {
    const date = task.scheduledDate ? parseDateKey(task.scheduledDate) : new Date();
    const weekday = toWeekdayNumber(date || new Date());
    if (!groups.has(weekday)) {
      groups.set(weekday, []);
    }
    groups.get(weekday).push(task);
  });

  return weekDays(today, weekStart)
    .map((day, index) => ({ ...day, label: weekdayNames[index], tasks: groups.get(index + 1) || [] }))
    .filter((group) => !group.isPast || group.tasks.length > 0);
};

export const isThisWeekCategory = (category) => category.label === "This week";
export const isPlanningWeekCategory = category => ['This week', 'Next week'].includes(category?.label);
export const categoryWeekGroups = category => groupTasksByWeekday(category.tasks, new Date(), parseDateKey(category.id.slice(5)));

export const deriveCategories = (tasks, today = new Date()) => {
  const now = new Date(today);
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
  const twoWeeksKey = formatDateKey(relativeWeekStart(2, now));

  weekKeys.forEach((key) => {
    const weekDate = parseDateKey(key);
    if (!weekDate) {
      return;
    }
    if (weekDate < currentWeekStart) {
      pastWeeks.push(key);
      return;
    }
    if (key === thisWeekKey || key === nextWeekKey || key === twoWeeksKey) {
      return;
    }
    futureWeeks.push(key);
  });

  pastWeeks.forEach((key) => {
    categories.push({ id: `week-${key}`, label: `Week starting ${key}`, tasks: weekGroups.get(key) });
  });

  categories.push({ id: `week-${thisWeekKey}`, label: "This week", tasks: weekGroups.get(thisWeekKey) || [] });

  categories.push({ id: `week-${nextWeekKey}`, label: "Next week", tasks: weekGroups.get(nextWeekKey) || [] });

  if (weekGroups.has(twoWeeksKey)) {
    categories.push({ id: `week-${twoWeeksKey}`, label: "In two weeks", tasks: weekGroups.get(twoWeeksKey) });
  }

  futureWeeks.forEach((key) => {
    categories.push({ id: `week-${key}`, label: `Week starting ${key}`, tasks: weekGroups.get(key) });
  });

  if (noDate.length) {
    categories.push({ id: "no-date", label: "No date", tasks: noDate });
  }

  if (notes.length) {
    categories.push({ id: "notes", label: "Notes", tasks: notes });
  }

  if (repeatable.length) {
    categories.push({ id: "repeatable", label: "Repeatable", tasks: repeatable });
  }

  return categories;
};
