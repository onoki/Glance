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
              <div class="task-item draft">
                <label class="task-check">
                  <input type="checkbox" disabled />
                  <span></span>
                </label>
                <div class="task-body">
                  <input
                    class="task-title"
                    v-model="draftTitle"
                    @input="createDraftIfNeeded"
                    placeholder="New task"
                  />
                  <textarea
                    class="task-content"
                    v-model="draftContent"
                    rows="2"
                    placeholder="Start typing to create"
                  ></textarea>
                </div>
              </div>
              <TaskItem
                v-for="task in newTasks"
                :key="task.id"
                :task="task"
                :on-save="saveTask"
                :on-complete="toggleComplete"
                :on-dirty="handleDirtyChange"
              />
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
                :on-save="saveTask"
                :on-complete="toggleComplete"
                :on-dirty="handleDirtyChange"
              />
              <button class="add-task" @click="createTask('dashboard:main')">Add task</button>
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
import { onBeforeUnmount, onMounted, ref } from "vue";
import { apiGet, apiPost, apiPut } from "./api";
import TaskItem from "./components/TaskItem.vue";

const tabs = ["Dashboard", "History", "Search", "Settings"];
const activeTab = ref("Dashboard");

const newTasks = ref([]);
const mainTasks = ref([]);
const expandedNew = ref(false);
const lastChangeId = ref(0);

let pollTimer = null;
const dirtySnapshots = new Map();

const draftTitle = ref("");
const draftContent = ref("");
const creatingDraft = ref(false);

const extractText = (node) => {
  if (!node) return "";
  if (Array.isArray(node)) {
    return node.map(extractText).join(" ");
  }
  if (typeof node === "object") {
    if (node.text) {
      return node.text;
    }
    if (node.content) {
      return extractText(node.content);
    }
  }
  return "";
};

const toContentDoc = (text) => {
  if (!text) {
    return { type: "doc", content: [] };
  }
  return {
    type: "doc",
    content: [
      {
        type: "paragraph",
        content: [{ type: "text", text }]
      }
    ]
  };
};

const normalizeTask = (task) => ({
  ...task,
  contentText: extractText(task.content)
});

const loadDashboard = async () => {
  const data = await apiGet("/api/dashboard");
  newTasks.value = mergeTasks(data.newTasks.map(normalizeTask), "dashboard:new");
  mainTasks.value = mergeTasks(data.mainTasks.map(normalizeTask), "dashboard:main");
};

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

const createTask = async (page, titleOverride, contentOverride) => {
  await apiPost("/api/tasks", {
    page,
    title: titleOverride || "New task",
    content: toContentDoc(contentOverride || ""),
    position: Date.now()
  });
  await loadDashboard();
};

const saveTask = async ({ id, title, contentText, baseUpdatedAt, page }) => {
  const response = await apiPut(`/api/tasks/${id}`, {
    baseUpdatedAt,
    title,
    content: toContentDoc(contentText),
    page
  });
  updateTaskLocal(id, { title, contentText, updatedAt: response.updatedAt });
  if (response.externalUpdate) {
    await loadDashboard();
  }
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
      content: toContentDoc(task.contentText),
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

const createDraftIfNeeded = async () => {
  if (creatingDraft.value) {
    return;
  }
  const title = draftTitle.value.trim();
  if (!title) {
    return;
  }
  creatingDraft.value = true;
  await createTask("dashboard:new", title, draftContent.value);
  draftTitle.value = "";
  draftContent.value = "";
  creatingDraft.value = false;
};

const pollChanges = async () => {
  const data = await apiGet(`/api/changes?since=${lastChangeId.value}`);
  lastChangeId.value = data.lastId;
  if (data.changes.length > 0) {
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
  padding: 28px 32px 40px;
}

.dashboard {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 24px;
  align-items: start;
}

.list-column.expanded {
  grid-column: 1 / -1;
}

.list-card {
  background: #fffaf3;
  border-radius: 20px;
  padding: 20px;
  box-shadow: 0 10px 30px rgba(31, 27, 22, 0.08);
}

.list-header {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 16px;
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
  gap: 12px;
}

.task-item {
  display: grid;
  grid-template-columns: 28px 1fr;
  gap: 12px;
  padding: 12px;
  border-radius: 16px;
  background: #fef7ed;
  border: 1px solid #efe1d0;
}

.task-item.completed {
  color: #8a8177;
  text-decoration: line-through;
}

.task-item.draft {
  border-style: dashed;
  background: #fff7eb;
}

.task-check {
  display: grid;
  place-items: center;
}

.task-check input {
  display: none;
}

.task-check span {
  width: 18px;
  height: 18px;
  border: 2px solid #6f665f;
  border-radius: 6px;
  display: inline-block;
  position: relative;
}

.task-check input:checked + span::after {
  content: "";
  position: absolute;
  inset: 3px;
  background: #6f665f;
  border-radius: 3px;
}

.task-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.task-title {
  border: none;
  background: transparent;
  font-weight: 600;
  font-size: 1rem;
  padding: 0;
  outline: none;
}

.task-content {
  border: none;
  resize: vertical;
  background: rgba(255, 255, 255, 0.7);
  border-radius: 10px;
  padding: 6px 10px;
  font-size: 0.85rem;
  outline: none;
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
