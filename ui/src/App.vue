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

            <div class="task-list">
              <TaskItem
                v-for="task in newTasks"
                :key="task.id"
                :task="task"
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
              <TaskItem
                v-for="task in mainTasks"
                :key="task.id"
                :task="task"
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
              <button class="add-task" @click="createEmptyTask('dashboard:main')">Add task</button>
            </div>
          </section>
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
import { onBeforeUnmount, onMounted, ref, watch } from "vue";
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

let pollTimer = null;
const dirtySnapshots = new Map();

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

const normalizeTask = (task) => ({
  ...task,
  title: normalizeTitle(task.title),
  content: normalizeContent(task.content)
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
  const list = task.page === "dashboard:new" ? newTasks.value : mainTasks.value;
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
  const list = task.page === "dashboard:new" ? newTasks.value : mainTasks.value;
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

const pollChanges = async () => {
  const previous = lastChangeId.value;
  const data = await apiGet(`/api/changes?since=${previous}`);
  lastChangeId.value = data.lastId;
  if (data.lastId > previous) {
    await loadDashboard();
  }
};

onMounted(async () => {
  await loadDashboard();
  pollTimer = setInterval(pollChanges, 750);
});

onBeforeUnmount(() => {
  if (pollTimer) {
    clearInterval(pollTimer);
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
