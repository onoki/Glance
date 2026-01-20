<template>
  <div class="app-shell">
    <header class="top-nav">
      <div class="brand">Glance</div>
      <nav class="tabs">
        <button
          v-for="tab in tabs"
          :key="tab"
          class="tab"
          :class="{ active: activeTab === tab }"
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </nav>
    </header>

    <main class="content">
      <section v-if="activeTab === 'Dashboard'" class="dashboard">
        <div class="list-column" :class="{ expanded: expandedNew }">
          <section class="list-card">
            <header class="list-header">
              <div>
                <h2>New tasks</h2>
                <p class="subtitle">Fresh captures that still need a home.</p>
              </div>
              <div class="header-actions">
                <button class="ghost" @click="moveNewToMain" :disabled="newTasks.length === 0">
                  Move to Uncategorized
                </button>
                <button class="ghost" @click="expandedNew = !expandedNew">
                  {{ expandedNew ? "Restore view" : "Expand" }}
                </button>
              </div>
            </header>

            <div class="task-list" @dragover.prevent @drop.prevent="dropOnCategory('new', 'dashboard:new', $event)">
              <TaskItem
                v-for="task in newTasks"
                :key="task.id"
                :task="task"
                :show-recurrence-controls="false"
                :on-set-recurrence="setTaskRecurrence"
                :draggable="true"
                :is-drop-target="dragOver.id === task.id"
                :drop-position="dragOver.position"
                drag-category-id="new"
                :on-drag-start="startDrag"
                :on-drag-end="endDrag"
                :on-drop="dropOnTask"
                :on-drag-over="setDragOver"
                :on-drag-leave="clearDragOver"
                :focus-title-id="focusTaskId"
                :focus-content-target="focusContentTarget"
                :on-save="saveTask"
                :on-complete="toggleComplete"
                :on-dirty="handleDirtyChange"
                :on-create-below="createTaskBelow"
                :on-tab-to-previous="moveTaskToPrevious"
                :on-split-to-new-task="splitSubcontentToNewTask"
                :on-focus-prev-task-from-title="focusPrevTaskFromTitle"
                :on-focus-next-task-from-content="focusNextTaskFromContent"
              />
              <button class="add-task" @click="createEmptyTask('dashboard:new')">Add task</button>
            </div>
          </section>
        </div>

        <div v-if="!expandedNew" class="list-column">
          <section class="list-card">
            <header class="list-header">
              <div>
                <h2>Main tasks</h2>
                <p class="subtitle">Your active focus for the day.</p>
              </div>
            </header>

            <div class="task-list">
              <div
                v-for="category in mainCategories"
                :key="category.id"
                class="category-block"
                @dragover.prevent
                @drop.prevent="dropOnCategory(category.id, 'dashboard:main', $event)"
              >
                <h3 class="category-title">{{ category.label }}</h3>
                <TaskItem
                  v-for="task in category.tasks"
                  :key="task.id"
                  :task="task"
                  :show-category-actions="true"
                  :on-set-category="setTaskCategory"
                  :show-recurrence-controls="category.id === 'repeatable'"
                  :on-set-recurrence="setTaskRecurrence"
                  :draggable="true"
                  :is-drop-target="dragOver.id === task.id"
                  :drop-position="dragOver.position"
                  :drag-category-id="category.id"
                  :on-drag-start="startDrag"
                  :on-drag-end="endDrag"
                  :on-drop="dropOnTask"
                  :on-drag-over="setDragOver"
                  :on-drag-leave="clearDragOver"
                  :focus-title-id="focusTaskId"
                  :focus-content-target="focusContentTarget"
                  :on-save="saveTask"
                  :on-complete="toggleComplete"
                  :on-dirty="handleDirtyChange"
                  :on-create-below="createTaskBelow"
                  :on-tab-to-previous="moveTaskToPrevious"
                  :on-split-to-new-task="splitSubcontentToNewTask"
                  :on-focus-prev-task-from-title="focusPrevTaskFromTitle"
                  :on-focus-next-task-from-content="focusNextTaskFromContent"
                />
              </div>
              <button class="add-task" @click="createEmptyTask('dashboard:main')">Add task</button>
            </div>
          </section>
        </div>
      </section>

      <section v-else-if="activeTab === 'History'" class="history-view">
        <div class="history-toolbar">
          <button class="ghost" @click="moveCompletedToHistory">Debug: Move completed to history</button>
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
                  :on-complete="toggleComplete"
                  :on-dirty="noop"
                  :on-create-below="noop"
                  :on-tab-to-previous="noopAsync"
                  :on-split-to-new-task="noop"
                  :on-focus-prev-task-from-title="noop"
                  :on-focus-next-task-from-content="noop"
                />
              </div>
            </section>
          </div>
        </div>
      </section>

      <section v-else-if="activeTab === 'Search'" class="search-view">
        <div class="search-bar">
          <input
            v-model="searchQuery"
            type="search"
            placeholder="Search tasks"
            @keydown.enter.prevent="searchTasks"
          />
          <button class="add-task" :disabled="isSearching" @click="searchTasks">Search</button>
        </div>
        <div class="search-results">
          <div v-if="!hasSearched" class="search-empty"></div>
          <div v-else-if="searchResults.length === 0" class="search-empty">No results</div>
          <div v-else class="search-list">
            <TaskItem
              v-for="result in searchResults"
              :key="result.task.id"
              :task="result.task"
              :read-only="true"
              :allow-toggle="false"
              :focus-title-id="null"
              :focus-content-target="null"
              :on-save="noop"
              :on-complete="noop"
              :on-dirty="noop"
              :on-create-below="noop"
              :on-tab-to-previous="noopAsync"
              :on-split-to-new-task="noop"
              :on-focus-prev-task-from-title="noop"
              :on-focus-next-task-from-content="noop"
            />
          </div>
        </div>
      </section>

      <section v-else class="placeholder">
        <h2>{{ activeTab }}</h2>
        <p>This section will be implemented in the next iteration.</p>
      </section>
    </main>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { apiGet, apiPost, apiPut } from "./api";
import TaskItem from "./components/TaskItem.vue";

const tabs = ["Dashboard", "History", "Search", "Settings"];
const activeTab = ref("Dashboard");

const newTasks = ref([]);
const mainTasks = ref([]);
const expandedNew = ref(false);
const lastChangeId = ref(0);
const focusTaskId = ref(null);
const focusContentTarget = ref(null);
const searchQuery = ref("");
const searchResults = ref([]);
const hasSearched = ref(false);
const isSearching = ref(false);
const historyGroups = ref([]);
const historyStats = ref([]);
const currentDayKey = ref("");

let pollTimer = null;
let dayTimer = null;
const dirtySnapshots = new Map();
const recurrenceCache = new Map();
const dragState = ref(null);
const dragOver = ref({ id: null, position: "before" });

const emptyTitleDoc = () => ({
  type: "doc",
  content: [{ type: "paragraph" }]
});

const emptyDoc = () => ({
  type: "doc",
  content: [
    {
      type: "bulletList",
      content: [
        {
          type: "listItem",
          content: [{ type: "paragraph" }]
        }
      ]
    }
  ]
});

const normalizeContent = (content) => {
  if (!content || content.type !== "doc") {
    return emptyDoc();
  }
  const list = content.content?.[0];
  if (!list || list.type !== "bulletList") {
    return emptyDoc();
  }
  if (!list.content || list.content.length === 0) {
    return emptyDoc();
  }
  return content;
};

const titleFromText = (text) => ({
  type: "doc",
  content: [
    {
      type: "paragraph",
      content: text ? [{ type: "text", text }] : []
    }
  ]
});

const normalizeTitle = (title) => {
  if (typeof title === "string") {
    return titleFromText(title);
  }
  if (!title || title.type !== "doc") {
    return emptyTitleDoc();
  }
  if (!title.content || title.content.length === 0) {
    return emptyTitleDoc();
  }
  return title;
};

const titleDocToListItem = (titleDoc) => {
  const paragraphs = [];
  if (titleDoc?.content) {
    for (const node of titleDoc.content) {
      if (node.type === "paragraph") {
        paragraphs.push(node);
      }
    }
  }
  const inlineContent = [];
  paragraphs.forEach((para, index) => {
    if (para.content) {
      inlineContent.push(...para.content);
    }
    if (index < paragraphs.length - 1) {
      inlineContent.push({ type: "hardBreak" });
    }
  });
  return {
    type: "listItem",
    content: [
      {
        type: "paragraph",
        content: inlineContent.length ? inlineContent : []
      }
    ]
  };
};

const formatDateKey = (date) => {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, "0");
  const day = `${date.getDate()}`.padStart(2, "0");
  return `${year}-${month}-${day}`;
};

const getDayKey = () => formatDateKey(new Date());

const getWeekStart = (date) => {
  const day = date.getDay();
  const diff = (day + 6) % 7;
  const start = new Date(date);
  start.setDate(date.getDate() - diff);
  start.setHours(0, 0, 0, 0);
  return start;
};

const parseDateKey = (dateKey) => {
  const date = new Date(`${dateKey}T00:00:00`);
  if (Number.isNaN(date.getTime())) {
    return null;
  }
  return date;
};

const deriveCategories = (tasks) => {
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
  let thisWeekKey = null;
  let nextWeekKey = null;
  const nextWeek = new Date(currentWeekStart);
  nextWeek.setDate(currentWeekStart.getDate() + 7);
  thisWeekKey = formatDateKey(currentWeekStart);
  nextWeekKey = formatDateKey(nextWeek);

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

const escapeRegex = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

const tokenizeQuery = (query) =>
  query
    .split(/\s+/)
    .map((token) => token.trim())
    .filter((token) => token.length > 0);

const buildSearchRegex = (terms) => {
  const unique = Array.from(new Set(terms));
  if (unique.length === 0) {
    return null;
  }
  const pattern = unique.map(escapeRegex).join("|");
  return new RegExp(pattern, "gi");
};

const addSearchMark = (marks) => {
  const next = marks ? [...marks] : [];
  if (next.some((mark) => mark.type === "highlight" && mark.attrs?.color === "search")) {
    return next;
  }
  next.push({ type: "highlight", attrs: { color: "search" } });
  return next;
};

const highlightTextNode = (node, regex) => {
  if (!node.text || !regex) {
    return [node];
  }
  const text = node.text;
  const matches = [...text.matchAll(regex)];
  if (matches.length === 0) {
    return [node];
  }
  const parts = [];
  let lastIndex = 0;
  for (const match of matches) {
    const index = match.index ?? 0;
    const value = match[0] ?? "";
    if (index > lastIndex) {
      parts.push({ ...node, text: text.slice(lastIndex, index), marks: node.marks });
    }
    parts.push({ ...node, text: value, marks: addSearchMark(node.marks) });
    lastIndex = index + value.length;
  }
  if (lastIndex < text.length) {
    parts.push({ ...node, text: text.slice(lastIndex), marks: node.marks });
  }
  return parts;
};

const highlightNode = (node, regex) => {
  if (!node || typeof node !== "object") {
    return node;
  }
  if (node.type === "text") {
    return highlightTextNode(node, new RegExp(regex.source, regex.flags));
  }
  if (!node.content) {
    return { ...node };
  }
  const nextContent = [];
  for (const child of node.content) {
    const transformed = highlightNode(child, regex);
    if (Array.isArray(transformed)) {
      nextContent.push(...transformed);
    } else {
      nextContent.push(transformed);
    }
  }
  return { ...node, content: nextContent };
};

const highlightDoc = (doc, terms) => {
  const regex = buildSearchRegex(terms);
  if (!regex || !doc) {
    return doc;
  }
  return highlightNode(doc, regex);
};

const normalizeTask = (task) => ({
  ...task,
  title: normalizeTitle(task.title),
  content: normalizeContent(task.content)
});

const highlightTask = (task, terms) => ({
  ...task,
  title: highlightDoc(task.title, terms),
  content: highlightDoc(task.content, terms)
});

const mainCategories = computed(() => deriveCategories(mainTasks.value));

const findTaskById = (id) => {
  return [...newTasks.value, ...mainTasks.value].find((task) => task.id === id) || null;
};

const getCategoryTasks = (page, categoryId) => {
  if (page === "dashboard:new") {
    return newTasks.value;
  }
  if (page === "dashboard:main") {
    const category = mainCategories.value.find((item) => item.id === categoryId);
    return category ? category.tasks : [];
  }
  return [];
};

const getOrderedTasksForPage = (task) => {
  if (task.page === "dashboard:new") {
    return newTasks.value;
  }
  if (task.page === "dashboard:main") {
    return mainCategories.value.flatMap((category) => category.tasks);
  }
  return mainTasks.value;
};

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

const historySeries = computed(() => buildHistorySeries(historyStats.value || []));

const historyMax = computed(() => {
  return historySeries.value.reduce((max, item) => Math.max(max, item.count), 0);
});

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

const mergeTasks = (tasks, page) => {
  const merged = tasks.map((task) => {
    const snapshot = dirtySnapshots.get(task.id);
    return snapshot && snapshot.dirty ? snapshot.task : task;
  });

  for (const [id, snapshot] of dirtySnapshots.entries()) {
    if (!snapshot.dirty || snapshot.task.page !== page) {
      continue;
    }
    if (!merged.some((task) => task.id === id)) {
      merged.push(snapshot.task);
    }
  }

  return merged;
};

const loadDashboard = async () => {
  const data = await apiGet("/api/dashboard");
  newTasks.value = mergeTasks(data.newTasks.map(normalizeTask), "dashboard:new");
  mainTasks.value = mergeTasks(data.mainTasks.map(normalizeTask), "dashboard:main");
};

const loadHistory = async () => {
  const data = await apiGet("/api/history");
  historyStats.value = data.stats || [];
  historyGroups.value = (data.groups || []).map((group) => ({
    date: group.date,
    tasks: group.tasks.map(normalizeTask)
  }));
};

const insertTaskLocal = (task) => {
  const list = task.page === "dashboard:new" ? newTasks.value : mainTasks.value;
  const next = [...list, task].sort((a, b) => a.position - b.position);
  if (task.page === "dashboard:new") {
    newTasks.value = next;
  } else {
    mainTasks.value = next;
  }
};

const createTask = async (page, titleOverride, contentOverride, positionOverride) => {
  const payload = {
    page,
    title: normalizeTitle(titleOverride ?? emptyTitleDoc()),
    content: normalizeContent(contentOverride || emptyDoc()),
    position: positionOverride || Date.now()
  };
  const response = await apiPost("/api/tasks", payload);
  insertTaskLocal({
    id: response.taskId,
    page: payload.page,
    title: payload.title,
    content: payload.content,
    position: payload.position,
    createdAt: Date.now(),
    updatedAt: response.updatedAt,
    completedAt: null
  });
  await loadDashboard();
  return response.taskId;
};

const createTaskBelow = async (task) => {
  const list = task.page === "dashboard:new" ? newTasks.value : mainTasks.value;
  const index = list.findIndex((item) => item.id === task.id);
  const next = index >= 0 ? list[index + 1] : null;
  const position = next ? (task.position + next.position) / 2 : task.position + 1;
  return await createTask(task.page, emptyTitleDoc(), emptyDoc(), position);
};

const moveTaskToPrevious = async (task) => {
  const list = task.page === "dashboard:new" ? newTasks.value : mainTasks.value;
  const index = list.findIndex((item) => item.id === task.id);
  if (index <= 0) {
    return false;
  }

  const previous = list[index - 1];
  const prevContent = normalizeContent(previous.content);
  const prevList = prevContent.content?.[0]?.content ?? [];
  const currentContent = normalizeContent(task.content);
  const currentList = currentContent.content?.[0]?.content ?? [];
  const titleItem = titleDocToListItem(task.title);

  const merged = {
    type: "doc",
    content: [
      {
        type: "bulletList",
        content: [...prevList, titleItem, ...currentList]
      }
    ]
  };

  await saveTask({
    id: previous.id,
    title: previous.title,
    content: merged,
    baseUpdatedAt: previous.updatedAt,
    page: previous.page
  });

  await saveTask({
    id: task.id,
    title: task.title || "Untitled",
    content: currentContent,
    baseUpdatedAt: task.updatedAt,
    page: "dashboard:hidden"
  });

  focusContentTarget.value = {
    taskId: previous.id,
    listIndex: prevList.length,
    atEnd: true
  };

  newTasks.value = newTasks.value.filter((item) => item.id !== task.id);
  mainTasks.value = mainTasks.value.filter((item) => item.id !== task.id);

  return true;
};

const focusPrevTaskFromTitle = async (task) => {
  const list = getOrderedTasksForPage(task);
  const index = list.findIndex((item) => item.id === task.id);
  if (index <= 0) {
    return;
  }
  const previous = list[index - 1];
  const prevContent = normalizeContent(previous.content);
  const prevList = prevContent.content?.[0]?.content ?? [];
  const listIndex = Math.max(prevList.length - 1, 0);
  focusContentTarget.value = {
    taskId: previous.id,
    listIndex,
    atEnd: true
  };
};

const focusNextTaskFromContent = async (task) => {
  const list = getOrderedTasksForPage(task);
  const index = list.findIndex((item) => item.id === task.id);
  const next = index >= 0 ? list[index + 1] : null;
  if (next) {
    focusTaskId.value = next.id;
    return;
  }
  const newId = await createTaskBelow(task);
  focusTaskId.value = newId;
};

const splitSubcontentToNewTask = async (task, payload) => {
  const list = task.page === "dashboard:new" ? newTasks.value : mainTasks.value;
  const index = list.findIndex((item) => item.id === task.id);
  const next = index >= 0 ? list[index + 1] : null;
  const position = next ? (task.position + next.position) / 2 : task.position + 1;
  const newId = await createTask(task.page, payload.title, payload.content, position);
  focusTaskId.value = newId;
};

const saveTask = async ({ id, title, content, baseUpdatedAt, page }) => {
  const response = await apiPut(`/api/tasks/${id}`, {
    baseUpdatedAt,
    title: normalizeTitle(title),
    content: normalizeContent(content),
    page
  });
  updateTaskLocal(id, { title, content, updatedAt: response.updatedAt });
  await loadDashboard();
};

const toggleComplete = async (task) => {
  const completed = !task.completedAt;
  task.completedAt = completed ? Date.now() : null;
  await apiPost(`/api/tasks/${task.id}/complete`, { completed });
  await loadDashboard();
  if (activeTab.value === "History") {
    await loadHistory();
  }
};

const moveNewToMain = async () => {
  const updates = newTasks.value.map((task) =>
    apiPut(`/api/tasks/${task.id}`, {
      baseUpdatedAt: task.updatedAt,
      title: task.title,
      content: task.content,
      page: "dashboard:main"
    })
  );
  await Promise.all(updates);
  await loadDashboard();
};

const updateTaskLocal = (id, patch) => {
  const apply = (list) => {
    const task = list.find((item) => item.id === id);
    if (task) {
      Object.assign(task, patch);
    }
  };
  apply(newTasks.value);
  apply(mainTasks.value);
};

const handleDirtyChange = (id, dirty, snapshot) => {
  if (!id) {
    return;
  }
  if (!dirty) {
    dirtySnapshots.delete(id);
    return;
  }
  dirtySnapshots.set(id, { dirty: true, task: snapshot });
};

const createEmptyTask = async (page) => {
  const newId = await createTask(page, emptyTitleDoc(), emptyDoc(), Date.now());
  focusTaskId.value = newId;
};

const runRecurrenceGeneration = async () => {
  try {
    await apiPost("/api/recurrence/run", {});
  } catch {
    // ignore failures; dashboard refresh will retry later
  }
};

const setTaskCategory = async (task, category) => {
  let scheduledDate = undefined;
  let recurrence = undefined;
  const weekStart = getWeekStart(new Date());
  const cachedRecurrence = recurrenceCache.get(task.id);
  const existingRecurrence = task.recurrence;

  if (category !== "repeatable" && existingRecurrence && existingRecurrence.type !== "notes") {
    recurrenceCache.set(task.id, existingRecurrence);
  }

  switch (category) {
    case "uncategorized":
      scheduledDate = null;
      recurrence = null;
      break;
    case "notes":
      scheduledDate = null;
      recurrence = { type: "notes" };
      break;
    case "no-date":
      scheduledDate = "no-date";
      recurrence = null;
      break;
    case "this-week":
      scheduledDate = formatDateKey(weekStart);
      recurrence = null;
      break;
    case "next-week": {
      const nextWeek = new Date(weekStart);
      nextWeek.setDate(weekStart.getDate() + 7);
      scheduledDate = formatDateKey(nextWeek);
      recurrence = null;
      break;
    }
    case "repeatable":
      scheduledDate = null;
      recurrence = existingRecurrence && existingRecurrence.type !== "notes"
        ? existingRecurrence
        : cachedRecurrence || { type: "weekly", weekdays: [] };
      break;
    default:
      return;
  }

  await apiPut(`/api/tasks/${task.id}`, {
    baseUpdatedAt: task.updatedAt,
    scheduledDate,
    recurrence
  });
  if (recurrence) {
    await runRecurrenceGeneration();
  }
  await loadDashboard();
};

const setTaskRecurrence = async (task, recurrence) => {
  if (recurrence) {
    recurrenceCache.set(task.id, recurrence);
  }
  await apiPut(`/api/tasks/${task.id}`, {
    baseUpdatedAt: task.updatedAt,
    recurrence
  });
  await runRecurrenceGeneration();
  await loadDashboard();
};

const moveCompletedToHistory = async () => {
  await apiPost("/api/debug/move-completed-to-history", {});
  await loadDashboard();
  if (activeTab.value === "History") {
    await loadHistory();
  }
};

const buildCategoryUpdate = (task, categoryId) => {
  let scheduledDate = null;
  let recurrence = null;
  const cachedRecurrence = recurrenceCache.get(task.id);
  const existingRecurrence = task.recurrence;

  if (categoryId !== "repeatable" && existingRecurrence && existingRecurrence.type !== "notes") {
    recurrenceCache.set(task.id, existingRecurrence);
  }

  if (categoryId.startsWith("week-")) {
    scheduledDate = categoryId.slice(5);
    recurrence = null;
  } else {
    switch (categoryId) {
      case "uncategorized":
        scheduledDate = null;
        recurrence = null;
        break;
      case "notes":
        scheduledDate = null;
        recurrence = { type: "notes" };
        break;
      case "no-date":
        scheduledDate = "no-date";
        recurrence = null;
        break;
      case "repeatable":
        scheduledDate = null;
        recurrence = existingRecurrence && existingRecurrence.type !== "notes"
          ? existingRecurrence
          : cachedRecurrence || { type: "weekly", weekdays: [] };
        break;
      default:
        return null;
    }
  }

  return { scheduledDate, recurrence };
};

const applyTaskMove = async (task, categoryId, position) => {
  const update = {
    baseUpdatedAt: task.updatedAt,
    position
  };

  if (task.page === "dashboard:main") {
    const categoryUpdate = buildCategoryUpdate(task, categoryId);
    if (categoryUpdate) {
      update.scheduledDate = categoryUpdate.scheduledDate;
      update.recurrence = categoryUpdate.recurrence;
    }
  }

  await apiPut(`/api/tasks/${task.id}`, update);
  if (update.recurrence) {
    await runRecurrenceGeneration();
  }
  await loadDashboard();
};

const startDrag = (task, event) => {
  dragState.value = { taskId: task.id, page: task.page };
  dragOver.value = { id: task.id, position: "before" };
  if (event?.dataTransfer) {
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", task.id);
  }
};

const endDrag = () => {
  dragState.value = null;
  dragOver.value = { id: null, position: "before" };
};

const dropOnTask = async (targetTask, categoryId, event) => {
  const dragId = dragState.value?.taskId || event?.dataTransfer?.getData("text/plain");
  const dragged = dragId ? findTaskById(dragId) : null;
  if (!dragged || dragged.id === targetTask.id) {
    return;
  }
  if (dragged.page !== targetTask.page) {
    return;
  }

  const list = getCategoryTasks(targetTask.page, categoryId);
  const filtered = list.filter((item) => item.id !== dragged.id);
  const targetIndex = filtered.findIndex((item) => item.id === targetTask.id);
  if (targetIndex < 0) {
    return;
  }
  const dropPosition = dragOver.value.id === targetTask.id ? dragOver.value.position : "before";
  const before = dropPosition === "before";
  const prev = before ? (targetIndex > 0 ? filtered[targetIndex - 1] : null) : filtered[targetIndex];
  const next = before ? filtered[targetIndex] : (targetIndex + 1 < filtered.length ? filtered[targetIndex + 1] : null);
  const position = next && prev ? (prev.position + next.position) / 2 : prev ? prev.position + 1 : next.position - 1;
  await applyTaskMove(dragged, categoryId, position);
};

const dropOnCategory = async (categoryId, page, event) => {
  const dragId = dragState.value?.taskId || event?.dataTransfer?.getData("text/plain");
  const dragged = dragId ? findTaskById(dragId) : null;
  if (!dragged || dragged.page !== page) {
    return;
  }
  const list = getCategoryTasks(page, categoryId).filter((item) => item.id !== dragged.id);
  const last = list[list.length - 1] || null;
  const position = last ? last.position + 1 : Date.now();
  await applyTaskMove(dragged, categoryId, position);
};

const setDragOver = (taskId, position) => {
  dragOver.value = { id: taskId, position: position || "before" };
};

const clearDragOver = (taskId) => {
  if (dragOver.value.id === taskId) {
    dragOver.value = { id: null, position: "before" };
  }
};

const searchTasks = async () => {
  const query = searchQuery.value.trim();
  if (!query) {
    hasSearched.value = false;
    searchResults.value = [];
    return;
  }
  isSearching.value = true;
  try {
    const data = await apiGet(`/api/search?q=${encodeURIComponent(query)}`);
    const terms = tokenizeQuery(data.query || query);
    searchResults.value = data.results.map((result) => {
      const task = highlightTask(normalizeTask(result.task), terms);
      return { ...result, task };
    });
    hasSearched.value = true;
  } catch (error) {
    const message = error instanceof Error ? error.message : "Search failed";
    window.alert(message);
  } finally {
    isSearching.value = false;
  }
};

const noop = () => {};
const noopAsync = async () => false;

const pollChanges = async () => {
  const previous = lastChangeId.value;
  const data = await apiGet(`/api/changes?since=${previous}`);
  lastChangeId.value = data.lastId;
  if (data.lastId > previous) {
    await loadDashboard();
    if (activeTab.value === "History") {
      await loadHistory();
    }
  }
};

onMounted(async () => {
  await loadDashboard();
  if (activeTab.value === "History") {
    await loadHistory();
  }
  currentDayKey.value = getDayKey();
  pollTimer = setInterval(pollChanges, 750);
  dayTimer = setInterval(() => {
    const nextDay = getDayKey();
    if (nextDay !== currentDayKey.value) {
      currentDayKey.value = nextDay;
      runRecurrenceGeneration();
      loadDashboard();
      if (activeTab.value === "History") {
        loadHistory();
      }
    }
  }, 60000);
});

onBeforeUnmount(() => {
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  if (dayTimer) {
    clearInterval(dayTimer);
  }
});

watch(activeTab, (tab) => {
  if (tab === "History") {
    loadHistory();
  }
});

watch(focusTaskId, (id) => {
  if (!id) {
    return;
  }
  setTimeout(() => {
    if (focusTaskId.value === id) {
      focusTaskId.value = null;
    }
  }, 0);
});

watch(focusContentTarget, (target) => {
  if (!target) {
    return;
  }
  setTimeout(() => {
    if (focusContentTarget.value === target) {
      focusContentTarget.value = null;
    }
  }, 0);
});
</script>

<style scoped>
.app-shell {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

.top-nav {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 32px;
  background: #1f1b16;
  color: #f9f4ee;
}

.brand {
  font-size: 1.4rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

.tabs {
  display: flex;
  gap: 12px;
}

.tab {
  border: none;
  padding: 8px 16px;
  border-radius: 999px;
  background: transparent;
  color: inherit;
  font-size: 0.95rem;
  cursor: pointer;
  transition: background 0.2s ease;
}

.tab.active,
.tab:hover {
  background: rgba(249, 244, 238, 0.2);
}

.content {
  flex: 1;
  padding: 16px 24px 24px;
}

.dashboard {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
  align-items: start;
  background: #fbf6ef;
  border-radius: 16px;
  padding: 12px 16px;
}

.search-view {
  background: #fbf6ef;
  border-radius: 16px;
  padding: 16px;
  min-height: 60vh;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.search-bar {
  display: flex;
  gap: 8px;
}

.search-bar input {
  flex: 1;
  border: 1px solid #d8c7b3;
  border-radius: 10px;
  padding: 8px 10px;
  font-size: 0.95rem;
  background: #fffaf3;
}

.search-results {
  display: flex;
  flex-direction: column;
}

.search-empty {
  color: #6f665f;
  font-size: 0.95rem;
  padding: 8px 2px;
}

.search-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.list-column.expanded {
  grid-column: 1 / -1;
}

.list-card {
  background: transparent;
  border-radius: 12px;
  padding: 8px 4px;
  box-shadow: none;
}

.list-header {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 4px;
}

.list-header h2 {
  margin: 0 0 4px;
  font-size: 1.2rem;
}

.subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: #6f665f;
}

.header-actions {
  display: flex;
  gap: 8px;
  align-items: flex-start;
}

.ghost {
  border: 1px solid #c6b8a9;
  background: transparent;
  color: #3a3129;
  padding: 6px 10px;
  border-radius: 12px;
  cursor: pointer;
  font-size: 0.75rem;
}

.ghost:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.task-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.category-block {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin-bottom: 6px;
}

.category-title {
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #6f665f;
  margin: 8px 0 2px;
}

.history-view {
  background: #fbf6ef;
  border-radius: 16px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.history-toolbar {
  display: flex;
  justify-content: flex-end;
}

.history-chart {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.chart-area {
  display: grid;
  grid-template-columns: 1fr 32px;
  gap: 8px;
  align-items: end;
}

.chart-bars {
  display: grid;
  grid-template-columns: repeat(180, minmax(1px, 1fr));
  gap: 2px;
  align-items: end;
  height: 120px;
}

.chart-bar {
  background: #1f1b16;
  border-radius: 2px 2px 0 0;
  min-height: 2px;
}

.chart-axis {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: #6f665f;
}

.chart-scale {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  font-size: 0.7rem;
  color: #6f665f;
  text-align: right;
  height: 120px;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.history-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.history-date {
  font-size: 0.9rem;
  color: #6f665f;
  margin: 0;
}

.history-empty {
  color: #6f665f;
  font-size: 0.95rem;
}

.add-task {
  border: none;
  padding: 10px 12px;
  border-radius: 12px;
  background: #1f1b16;
  color: #f9f4ee;
  cursor: pointer;
  font-weight: 600;
}

.placeholder {
  background: #fffaf3;
  border-radius: 20px;
  padding: 30px;
  text-align: center;
  box-shadow: 0 10px 30px rgba(31, 27, 22, 0.08);
}

@media (max-width: 700px) {
  .top-nav {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
  }

  .tabs {
    flex-wrap: wrap;
  }

  .content {
    padding: 20px;
  }
}
</style>
