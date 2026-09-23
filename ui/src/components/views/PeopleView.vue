<template>
  <section ref="peopleRoot" class="people-view dashboard" @scroll.capture="keepTaskListsLeft">
    <header class="people-navigation">
      <button type="button" class="ghost" @click="addPerson">+ Person</button>
      <div class="people-tabs" role="tablist" aria-label="People">
        <div
          v-for="person in activePeople"
          :key="person.id"
          class="person-tab-shell"
          :class="{
            dragging: personDragId === person.id,
            'drop-target': personDropId === person.id,
            'drop-after': personDropId === person.id && personDropPosition === 'after'
          }"
          :draggable="true"
          title="Drag to reorder"
          @dragstart="startPersonDrag(person, $event)"
          @dragover.prevent="setPersonDrop(person, $event)"
          @drop.prevent="dropPerson(person)"
          @dragend="endPersonDrag"
        >
          <button
            type="button"
            class="tab person-tab"
            :class="{ active: viewMode === 'person' && selectedId === person.id }"
            role="tab"
            :aria-selected="viewMode === 'person' && selectedId === person.id"
            @click="selectPerson(person.id)"
          >
            {{ person.displayName }}
            <span v-for="tag in tagsFor(person)" :key="tag.id" class="person-tag-dot" :style="{ backgroundColor: tag.color || tagColor(tag.id) }" :title="tag.name" :aria-label="tag.name"></span>
          </button>
          <span class="person-tab-grip" aria-hidden="true">::</span>
        </div>
        <button
          type="button"
          class="tab archived-tab"
          :class="{ active: viewMode === 'archived' }"
          role="tab"
          :aria-selected="viewMode === 'archived'"
          @click="showArchive"
        >
          Archived ({{ archivedPeople.length }})
        </button>
      </div>
    </header>
    <div v-if="directory.tags.length" class="people-tag-legend" aria-label="Person tag colors">
      <span v-for="tag in directory.tags" :key="tag.id" class="people-tag-legend-item"><span class="person-tag-dot" :style="{ backgroundColor: tag.color || tagColor(tag.id) }" aria-hidden="true"></span><span class="people-tag-label">{{ tag.name }}</span></span>
    </div>

    <section v-if="viewMode === 'archived'" class="archive-panel" aria-label="Archived people">
      <header class="archive-header">
        <h2>Archived people</h2>
        <p>These people are separate from completed notes in History.</p>
        <p v-if="archiveNavigationNotice" class="archive-navigation-notice">{{ archiveNavigationNotice }}</p>
      </header>
      <div v-if="archivedPeople.length" class="archive-list">
        <article
          v-for="person in archivedPeople"
          :key="person.id"
          class="archived-person-card"
          :class="{ 'navigation-highlight': archivedNavigationPersonId === person.id }"
          :data-person-id="person.id"
        >
          <div>
            <strong>{{ person.displayName }}</strong>
            <span v-if="tagNamesFor(person).length" class="archived-tags">{{ tagNamesFor(person).join(", ") }}</span>
          </div>
          <button type="button" class="ghost" @click="restorePerson(person)">Restore</button>
        </article>
      </div>
      <p v-else class="empty-message">No archived people.</p>
    </section>

    <div v-else-if="selectedPerson" class="person-panel">
      <header class="person-controls">
        <h2 class="selected-person-name">{{ selectedPerson.displayName }}</h2>
        <details class="person-tags-menu">
          <summary class="ghost tag-summary">
            Tags<span v-if="selectedTagNames.length">: {{ selectedTagNames.join(", ") }}</span>
          </summary>
          <div class="tag-menu-panel">
            <strong>Tags for {{ selectedPerson.displayName }}</strong>
            <div v-for="tag in directory.tags" :key="tag.id" class="tag-menu-row">
              <label>
                <input
                  type="checkbox"
                  :checked="selectedPerson.tagIds.includes(tag.id)"
                  @change="togglePersonTag(tag.id, $event.target.checked)"
                />
                <span>{{ tag.name }}</span>
              </label>
            </div>
            <p v-if="!directory.tags.length" class="empty-message">No tags yet.</p>
            <details class="shared-tag-settings">
              <summary>Manage shared tags</summary>
              <p class="empty-message">Rename, color, and deletion affect everyone using the tag.</p>
              <div v-for="tag in directory.tags" :key="tag.id" class="tag-menu-row">
                <span>{{ tag.name }}</span>
              <input type="color" :value="tag.color || tagColor(tag.id)" :aria-label="`Color for ${tag.name}`" title="Colors are saved automatically; click outside to close" @input="changeTagColor(tag, $event.target.value)" @change="changeTagColor(tag, $event.target.value)" />
              <button type="button" class="tiny-action" title="Rename tag" @click="renameTag(tag)">Rename</button>
              <button type="button" class="delete-task task-icon-button" title="Delete tag" :aria-label="`Delete tag ${tag.name}`" @click="removeTag(tag)">×</button>
              </div>
            <button type="button" class="ghost tag-add" @click="addTag">+ New tag</button>
            </details>
          </div>
        </details>
        <button type="button" class="ghost" @click="renamePerson">Rename</button>
        <button type="button" class="ghost" @click="archivePerson">Archive</button>
      </header>

      <div class="person-task-list task-list">
        <TransitionGroup :key="selectedId" name="task-move" tag="div" class="task-list-group">
          <TaskItem
            v-for="(task, index) in tasks"
            :key="task.id"
            :task="task"
            :clipboard-adapter="peopleClipboard"
            :show-owner-person="false"
            :draggable="true"
            :is-last-in-category="index === tasks.length - 1"
            :is-drop-target="dragOver.id === task.id"
            :drop-position="dragOver.position"
            drag-category-id="people"
            :focus-title-id="focusTaskId"
            :undo-signal="undoSignal"
            :focus-content-target="focusContentTarget"
            :highlight-id="navigationHighlightId"
            :highlight-nonce="navigationHighlightNonce"
            :on-save="saveTask"
            :on-complete="toggleComplete"
            :on-dirty="handleDirtyChange"
            :on-create-below="createTaskBelow"
            :on-split-title-to-new-task="splitTitleToNewTask"
            :on-tab-to-previous="moveTaskToPrevious"
            :on-merge-to-previous="mergeTaskToPrevious"
            :on-merge-with-next="mergeTaskWithNext"
            :on-split-to-new-task="splitSubcontentToNewTask"
            :on-focus-prev-task-from-title="focusPreviousTask"
            :on-focus-next-task-from-content="focusNextTask"
            :on-delete="removeTask"
            :on-drag-start="startDrag"
            :on-drag-end="endDrag"
            :on-drop="dropOnTask"
            :on-drag-over="setDragOver"
            :on-drag-leave="clearDragOver"
            :on-send-to-dashboard="sendDashboard"
            :on-load-send-events="loadSendEvents"
            :on-dismiss-send-marker="dismissSend"
          />
        </TransitionGroup>
        <button class="task-list-tail" type="button" aria-label="Add a note for this person" @click="appendPersonTask"><span>+ Add note</span></button>
        <p v-if="!tasks.length" class="empty-message">Preparing an empty note…</p>
      </div>
    </div>

    <section v-else class="people-empty">
      <p>Add a person to start a private list of notes and questions.</p>
    </section>
  </section>
</template>

<script setup>
import { createTaskClipboardAdapter } from "../../services/taskClipboardAdapter.js";
import { taskClipboard } from "../../services/taskClipboard.js";
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import TaskItem from "../TaskItem.vue";
import {
  createPerson,
  createPersonTag,
  deletePersonTag,
  fetchPeople,
  setPersonTags,
  updatePerson,
  updatePersonTag
} from "../../api/people.js";
import { dismissTaskSendMarker, fetchTaskSendEvents, sendTaskToDashboard } from "../../api/taskSend.js";
import { usePeopleTasks } from "../../composables/usePeopleTasks.js";
import { tagColor, createTagColorSaver } from "../../utils/tagColor.js";
import { keepTaskListsLeft } from "../../utils/taskListScroll.js";
import { flushAllSaves } from "../../services/saveCoordinator.js";

const props = defineProps({
  taskHistory: { type: Object, default: () => ({ undo: [], redo: [] }) },
  navigationTarget: { type: Object, default: null }
});

const emit = defineEmits(["directory-change", "history-change"]);
const directory = ref({ people: [], tags: [] });
const peopleRoot = ref(null);
const selectedId = ref(sessionStorage.getItem("glance.selectedPerson"));
watch(selectedId, (id) => {
  if (id) sessionStorage.setItem("glance.selectedPerson", id);
  else sessionStorage.removeItem("glance.selectedPerson");
});
const viewMode = ref("person");
let appending = false;
const appendPersonTask = async () => {
  if (appending || !tasks.value.length) return;
  appending = true;
  try { await createTaskBelow(tasks.value[tasks.value.length - 1]); }
  finally { appending = false; }
};
const archiveNavigationNotice = ref("");
const archivedNavigationPersonId = ref(null);
const navigationHighlightId = ref(null);
const navigationHighlightNonce = ref(0);
const personDragId = ref(null);
const personDropId = ref(null);
const personDropPosition = ref("before");
let pollTimer = null;
let polling = false;
let viewReady = false;
let pendingNavigationTarget = null;
let navigationSequence = 0;

const activePeople = computed(() => directory.value.people
  .filter((person) => !person.archivedAt)
  .sort((left, right) => left.position - right.position));
const archivedPeople = computed(() => directory.value.people
  .filter((person) => !!person.archivedAt)
  .sort((left, right) => left.displayName.localeCompare(right.displayName)));
const selectedPerson = computed(() => activePeople.value.find((person) => person.id === selectedId.value) || null);
const selectedTagNames = computed(() => {
  const selected = new Set(selectedPerson.value?.tagIds || []);
  return directory.value.tags.filter((tag) => selected.has(tag.id)).map((tag) => tag.name);
});

const {
  recordClipboard,
  tasks,
  focusTaskId,
  undoSignal,
  focusContentTarget,
  dragOver,
  loadTasks,
  undo,
  redo,
  saveTask,
  removeTask,
  toggleComplete,
  createTaskBelow,
  moveTaskToPrevious,
  mergeTaskToPrevious,
  mergeTaskWithNext,
  focusPreviousTask,
  focusNextTask,
  splitSubcontentToNewTask,
  splitTitleToNewTask,
  handleDirtyChange,
  startDrag,
  endDrag,
  setDragOver,
  clearDragOver,
  dropOnTask
} = usePeopleTasks({
  history: props.taskHistory,
  selectedPersonId: selectedId,
  onHistoryChange: () => emit("history-change")
});

const peopleClipboard = createTaskClipboardAdapter({
  tasks: () => tasks.value, refresh: loadTasks,
  record: (entry) => {
    const personId = selectedId.value;
    recordClipboard({
      undo: async () => { selectedId.value = personId; return entry.undo(); },
      redo: async () => { selectedId.value = personId; return entry.redo(); }
    });
  }
});
const clipboardTarget = () => selectedId.value ? {
  adapter: peopleClipboard,
  snapshot: () => ({ page: 'people:main', ownerPersonId: selectedId.value, position: 0 })
} : null;

watch(focusTaskId, (id) => {
  if (!id) return;
  setTimeout(() => {
    if (focusTaskId.value === id) focusTaskId.value = null;
  }, 0);
});

watch(focusContentTarget, (target) => {
  if (!target) return;
  setTimeout(() => {
    if (focusContentTarget.value === target) focusContentTarget.value = null;
  }, 0);
});

const loadDirectory = async () => {
  directory.value = await fetchPeople(true);
  if (!activePeople.value.some((person) => person.id === selectedId.value)) {
    selectedId.value = activePeople.value[0]?.id || null;
  }
  emit("directory-change", directory.value);
};

const selectPerson = async (id) => {
  if (taskClipboard.busy.value) return;
  archiveNavigationNotice.value = "";
  archivedNavigationPersonId.value = null;
  viewMode.value = "person";

  await loadTasks(id);
};

const showArchive = () => {
  archiveNavigationNotice.value = "";
  archivedNavigationPersonId.value = null;
  viewMode.value = "archived";
};

const scrollToElement = (selector, id) => {
  requestAnimationFrame(() => {
    const element = Array.from(peopleRoot.value?.querySelectorAll?.(selector) || [])
      .find((candidate) => candidate.dataset.taskId === id || candidate.dataset.personId === id);
    element?.scrollIntoView?.({ block: "center", inline: "nearest", behavior: "smooth" });
  });
};

const applyNavigationTarget = async (target) => {
  if (!target?.taskId || !target?.personId) return;
  const sequence = ++navigationSequence;
  await loadDirectory();
  if (sequence !== navigationSequence) return;
  const person = directory.value.people.find((candidate) => candidate.id === target.personId);
  if (!person) {
    window.alert("The person for this search result no longer exists.");
    return;
  }

  if (person.archivedAt) {
    viewMode.value = "archived";
    archivedNavigationPersonId.value = person.id;
    navigationHighlightId.value = null;
    archiveNavigationNotice.value = `The matching note belongs to archived person ${person.displayName}. Restore the person to open the note.`;
    await nextTick();
    scrollToElement("[data-person-id]", person.id);
    return;
  }

  archiveNavigationNotice.value = "";
  archivedNavigationPersonId.value = null;
  viewMode.value = "person";
  selectedId.value = person.id;
  await loadTasks();
  if (sequence !== navigationSequence) return;
  navigationHighlightId.value = target.taskId;
  navigationHighlightNonce.value += 1;
  await nextTick();
  scrollToElement("[data-task-id]", target.taskId);
};

const addPerson = async () => {
  const name = window.prompt("Person name");
  if (!name?.trim()) return;
  const person = await createPerson(name.trim());
  await loadDirectory();
  selectedId.value = person.id;
  viewMode.value = "person";
  await loadTasks();
};

const renamePerson = async () => {
  const name = window.prompt("New name", selectedPerson.value.displayName);
  if (!name?.trim()) return;
  await updatePerson(selectedPerson.value.id, { displayName: name.trim() });
  await loadDirectory();
};

const archivePerson = async () => {
  const person = selectedPerson.value;
  if (!person || !window.confirm(`Archive ${person.displayName}? Notes and history will be preserved.`)) return;
  await updatePerson(person.id, { archived: true });
  await loadDirectory();
  await loadTasks();
};

const restorePerson = async (person) => {
  await updatePerson(person.id, { archived: false });
  await loadDirectory();
  if (props.navigationTarget?.personId === person.id) {
    await applyNavigationTarget(props.navigationTarget);
  }
};

const addTag = async () => {
  const name = window.prompt("Tag name (for example, Team members)");
  if (!name?.trim()) return;
  await createPersonTag(name.trim());
  await loadDirectory();
};

const saveTagColor = createTagColorSaver((id, color) => updatePersonTag(id, { color }));
const changeTagColor = async (tag, color) => {
  try {
    const saved = await saveTagColor(tag.id, color);
    const current = directory.value.tags.find((item) => item.id === tag.id);
    if (current) current.color = saved;
    emit("directory-change", directory.value);
  } catch {
    window.alert("Could not save the tag color. Please choose it again.");
  }
};

const renameTag = async (tag) => {
  const name = window.prompt("New tag name", tag.name);
  if (!name?.trim()) return;
  await updatePersonTag(tag.id, { name: name.trim() });
  await loadDirectory();
};

const removeTag = async (tag) => {
  if (!window.confirm(`Delete tag “${tag.name}”? People and notes will remain.`)) return;
  await deletePersonTag(tag.id);
  await loadDirectory();
};

const togglePersonTag = async (tagId, checked) => {
  const ids = new Set(selectedPerson.value.tagIds);
  if (checked) ids.add(tagId);
  else ids.delete(tagId);
  await setPersonTags(selectedPerson.value.id, [...ids]);
  await loadDirectory();
};

const startPersonDrag = (person, event) => {
  personDragId.value = person.id;
  personDropId.value = person.id;
  personDropPosition.value = "before";
  event.dataTransfer.effectAllowed = "move";
  event.dataTransfer.setData("text/plain", person.id);
};

const endPersonDrag = () => {
  personDragId.value = null;
  personDropId.value = null;
  personDropPosition.value = "before";
};

const setPersonDrop = (person, event) => {
  const rect = event.currentTarget?.getBoundingClientRect();
  personDropId.value = person.id;
  personDropPosition.value = rect && event.clientX >= rect.left + rect.width / 2 ? "after" : "before";
};

const dropPerson = async (target) => {
  const draggedId = personDragId.value;
  if (!draggedId || draggedId === target.id) {
    endPersonDrag();
    return;
  }
  const reordered = activePeople.value.filter((person) => person.id !== draggedId);
  const targetIndex = reordered.findIndex((person) => person.id === target.id);
  const dragged = activePeople.value.find((person) => person.id === draggedId);
  const insertAt = targetIndex + (personDropPosition.value === "after" ? 1 : 0);
  reordered.splice(insertAt, 0, dragged);
  const positioned = reordered.map((person, index) => ({ ...person, position: (index + 1) * 1000 }));
  const archived = directory.value.people.filter((person) => !!person.archivedAt);
  directory.value = { ...directory.value, people: [...positioned, ...archived] };
  endPersonDrag();
  await Promise.all(positioned.map((person) => updatePerson(person.id, { position: person.position })));
  await loadDirectory();
};

const tagNamesFor = (person) => {
  const ids = new Set(person.tagIds || []);
  return directory.value.tags.filter((tag) => ids.has(tag.id)).map((tag) => tag.name);
};
const tagsFor = (person) => directory.value.tags.filter((tag) => (person.tagIds || []).includes(tag.id));

const sendDashboard = async (task) => {
  await sendTaskToDashboard(task.id);
  task.sendMarkerVisible = true;
};
const loadSendEvents = (task) => fetchTaskSendEvents(task.id);
const dismissSend = async (task) => {
  await dismissTaskSendMarker(task.id);
  task.sendMarkerVisible = false;
};

const poll = async () => {
  if (polling || document.visibilityState === "hidden") return;
  polling = true;
  try {
    await loadDirectory();
    if (viewMode.value === "person") await loadTasks();
  } catch {
    // A later poll or a local action will retry.
  } finally {
    polling = false;
  }
};

onMounted(async () => {
  await loadDirectory();
  await loadTasks();
  viewReady = true;
  if (pendingNavigationTarget) {
    const target = pendingNavigationTarget;
    pendingNavigationTarget = null;
    await applyNavigationTarget(target);
  }
  pollTimer = window.setInterval(poll, 3000);
});

watch(
  () => props.navigationTarget,
  (target) => {
    if (!target?.taskId) return;
    if (!viewReady) {
      pendingNavigationTarget = target;
      return;
    }
    void applyNavigationTarget(target);
  },
  { immediate: true }
);

onBeforeUnmount(() => {
  if (pollTimer) window.clearInterval(pollTimer);
});

const undoFromShortcut = async () => {
  try {
    if (!(await flushAllSaves()).ok) return false;
    return await undo();
  }
  catch (error) {
    window.alert(error instanceof Error ? error.message : "Could not undo the People note change.");
    return false;
  }
};

const redoFromShortcut = async () => {
  try {
    if (!(await flushAllSaves()).ok) return false;
    return await redo();
  }
  catch (error) {
    window.alert(error instanceof Error ? error.message : "Could not redo the People note change.");
    return false;
  }
};

defineExpose({ undo: undoFromShortcut, redo: redoFromShortcut, clipboardTarget });
</script>

<style scoped>
.people-view {
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
  padding: 0;
}

.archive-navigation-notice {
  color: var(--text-main);
  margin: 2px 0 0;
}

.archived-person-card.navigation-highlight {
  animation: archived-person-flash 0.8s ease-out;
  outline: 1px solid rgba(120, 92, 40, 0.35);
}

@keyframes archived-person-flash {
  from { background: rgba(225, 199, 128, 0.55); }
  to { background: transparent; }
}

.people-navigation {
  display: flex;
  align-items: flex-start;
  gap: 4px;
  padding: 3px 4px;
  border-bottom: 1px solid var(--border-panel);
  background: var(--bg-header);
}

.people-tabs {
  display: flex;
  align-items: stretch;
  gap: 1px;
  min-width: 0;
  flex: 1;
  flex-wrap: wrap;
}

.people-navigation > .add-task { flex: 0 0 auto; font-family: inherit; font-size: var(--font-size-meta); font-weight: 400; }
.person-tab { display: inline-flex; align-items: center; gap: 3px; flex-wrap: wrap; overflow-wrap: anywhere; }
.person-tab-shell { max-width: 100%; min-width: 0; }
.person-tag-dot { display: inline-block; width: 7px; height: 7px; flex: 0 0 7px; border-radius: 50%; border: 1px solid #0003; }
.people-tag-legend { display: flex; flex-wrap: wrap; gap: 4px 10px; padding: 2px 4px; font-size: var(--font-size-meta); }
.people-tag-legend-item { display: inline-flex; align-items: center; gap: 3px; line-height: 1; }
.people-tag-label { display: inline-block; line-height: 1; }

.person-tab-shell {
  display: inline-flex;
  align-items: center;
  flex: 0 0 auto;
  border: 1px solid transparent;
  cursor: grab;
}

.person-tab-shell.dragging { opacity: 0.45; }
.person-tab-shell.drop-target { border-left-color: var(--focus-outline); }
.person-tab-shell.drop-target.drop-after { border-left-color: transparent; border-right-color: var(--focus-outline); }
.person-tab-grip { color: var(--text-muted); font-size: var(--font-size-meta); padding-right: 3px; user-select: none; }
.archived-tab { margin-left: auto; flex: 0 0 auto; }

.person-panel {
  display: flex;
  flex: 1;
  min-height: 0;
  flex-direction: column;
  padding: 2px 4px 0;
}

.person-controls {
  display: flex;
  justify-content: flex-start;
  align-items: center;
  gap: 3px;
  min-height: 23px;
  position: relative;
  z-index: 3;
}

.selected-person-name { margin: 0 6px 0 0; font-size: var(--font-size-body); }
.shared-tag-settings { display: grid; gap: 4px; }
.shared-tag-settings > :not(summary) { margin-top: 4px; }
.person-tags-menu { position: relative; }
.person-tags-menu > summary { list-style: none; }
.person-tags-menu > summary::-webkit-details-marker { display: none; }
.tag-summary { max-width: 360px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; font: inherit; font-size: var(--font-size-body); padding: 2px 6px; line-height: normal; }

.tag-menu-panel {
  position: absolute;
  z-index: 1000;
  top: calc(100% + 1px);
  left: 0;
  min-width: 260px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 4px;
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-main);
}

.tag-menu-row {
  display: grid;
  grid-template-columns: minmax(110px, 1fr) auto auto auto;
  gap: 4px;
  align-items: center;
  font-size: var(--font-size-meta);
}

.tag-menu-row label { display: flex; align-items: center; gap: 4px; min-width: 0; }
.tiny-action { border: 0; background: transparent; color: var(--text-muted); padding: 1px 2px; font-size: var(--font-size-meta); cursor: pointer; }
.tag-add { align-self: flex-start; }

.person-task-list {
  flex: 1;
  min-height: 0;
  width: min(900px, 100%);
  overflow-y: auto;
  padding-right: 3px;
}

.archive-panel {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 8px;
}

.archive-header h2 { margin: 0 0 3px; font-size: var(--font-size-body); font-weight: 400; }
.archive-header p { margin: 0 0 8px; color: var(--text-muted); }
.archive-list { display: flex; flex-direction: column; gap: 3px; max-width: 640px; }
.archived-person-card { display: flex; justify-content: space-between; align-items: center; gap: 10px; padding: 4px; border: 1px solid var(--border-panel); background: var(--bg-panel); }
.archived-tags { display: block; color: var(--text-muted); font-size: var(--font-size-meta); margin-top: 2px; }
.empty-message, .people-empty { color: var(--text-muted); font-size: var(--font-size-meta); }
.people-empty { padding: 8px; }
</style>
