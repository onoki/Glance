import { taskDropPositions } from "./taskDrag.js";

const sameDate = (left, right) => (left?.scheduledDate ?? null) === (right?.scheduledDate ?? null);

// A group is a contiguous run within one rendered list (or one planning day).
export function taskGroup(rows, task) {
  if (!task?.groupId) return [];
  const index = rows.findIndex(row => row.id === task.id);
  if (index < 0) return [];
  let start = index;
  let end = index;
  while (rows[start - 1]?.groupId === task.groupId && sameDate(rows[start - 1], task)) start--;
  while (rows[end + 1]?.groupId === task.groupId && sameDate(rows[end + 1], task)) end++;
  return end > start ? rows.slice(start, end + 1) : [];
}

const groupSelection = (rows, task, selectedIds) => {
  if (selectedIds.size < 2 || !selectedIds.has(task.id)) return null;
  const selected = rows.filter(row => selectedIds.has(row.id));
  if (selected.length !== selectedIds.size || selected.some(row => !sameDate(row, task))) return null;
  let group = [];
  const members = new Set();
  for (const row of selected) {
    if (members.has(row.id)) continue;
    const siblings = taskGroup(rows, row);
    if (!siblings.length) continue;
    if (group.length || siblings.some(member => !selectedIds.has(member.id))) return null;
    group = siblings;
    for (const member of group) members.add(member.id);
  }
  return { selected, group };
};

export function taskGroupActions(rows, task, selectedIds) {
  const index = rows.findIndex(row => row.id === task.id);
  if (index < 0) return [];
  const selection = groupSelection(rows, task, selectedIds);
  if (selection?.group.length && selection.selected.length > selection.group.length) {
    return selection.group.some(row => row.id === task.id) ? ["extend", "detach"] : ["extend"];
  }
  if (taskGroup(rows, task).length) return ["detach"];
  if (selectedIds.size > 1 && selectedIds.has(task.id)) {
    if (!selection) return [];
    const first = rows.indexOf(selection.selected[0]);
    return selection.selected.every((row, offset) => row === rows[first + offset]) ? ["create"] : [];
  }
  return [
    ...(sameDate(rows[index - 1], task) && taskGroup(rows, rows[index - 1]).length ? ["attach-above"] : []),
    ...(sameDate(rows[index + 1], task) && taskGroup(rows, rows[index + 1]).length ? ["attach-below"] : [])
  ];
}

const extendGroup = (rows, selectedIds, { selected, group }) => {
  const first = group[0];
  const last = group.at(-1);
  const groupStart = rows.indexOf(first);
  const groupEnd = rows.indexOf(last);
  const selectedStart = selected.indexOf(first);
  const additionsAbove = selected.slice(0, selectedStart);
  const additionsBelow = selected.slice(selectedStart + group.length);
  const unselectedAbove = rows.slice(0, groupStart).filter(row => !selectedIds.has(row.id));
  const unselectedBelow = rows.slice(groupEnd + 1).filter(row => !selectedIds.has(row.id));
  const previous = unselectedAbove.at(-1);
  const next = unselectedBelow[0];
  const ranks = new Map();
  const place = (additions, index, left, right) => {
    if (additions.every((row, offset) => row === rows[index + offset])) return true;
    const positions = taskDropPositions(additions.length, left, right);
    const fits = positions.every((position, offset) => Number.isFinite(position)
      && position > (positions[offset - 1] ?? left?.position ?? -Infinity)
      && position < (right?.position ?? Infinity));
    if (fits) additions.forEach((row, offset) => ranks.set(row.id, positions[offset]));
    return fits;
  };
  const aboveFits = place(additionsAbove, groupStart - additionsAbove.length, previous, first);
  const belowFits = place(additionsBelow, groupEnd + 1, last, next);
  const groupId = first.groupId;
  if (aboveFits && belowFits) {
    return selected.map(task => ({
      task, groupId, ...(ranks.has(task.id) ? { position: ranks.get(task.id) } : {})
    }));
  }
  // Equal or very close positions can leave no room to interpolate.
  return [...unselectedAbove, ...selected, ...unselectedBelow].flatMap((task, position) =>
    selectedIds.has(task.id) ? [{ task, groupId, position }]
      : task.position !== position ? [{ task, position }] : []);
};

export function taskGroupChanges(rows, task, selectedIds, action, newId = () => crypto.randomUUID()) {
  if (!taskGroupActions(rows, task, selectedIds).includes(action)) {
    throw new Error("The group or selection changed. Select the tasks again.");
  }
  if (action === "create") {
    const groupId = newId();
    return rows.filter(row => selectedIds.has(row.id)).map(row => ({ task: row, groupId }));
  }
  if (action === "extend") return extendGroup(rows, selectedIds, groupSelection(rows, task, selectedIds));
  if (action !== "detach") {
    const index = rows.findIndex(row => row.id === task.id);
    const neighbor = rows[index + (action === "attach-above" ? -1 : 1)];
    return [task, ...taskGroup(rows, neighbor)].map(row => ({ task: row, groupId: neighbor.groupId }));
  }
  return detachTaskGroups(rows, selectedIds.has(task.id) ? selectedIds : new Set([task.id]));
}

export function detachTaskGroups(rows, selectedIds) {
  const visited = new Set();
  return rows.flatMap(task => {
    if (!selectedIds.has(task.id) || visited.has(task.id)) return [];
    const group = taskGroup(rows, task);
    if (!group.length) return [];
    group.forEach(row => visited.add(row.id));
    const detached = group.filter(row => selectedIds.has(row.id));
    const remaining = group.filter(row => !selectedIds.has(row.id));
    const order = [...remaining, ...detached];
    const reorder = remaining.length && order.some((row, index) => row !== group[index]);
    const last = group.at(-1);
    const positions = reorder ? taskDropPositions(detached.length, last, rows[rows.indexOf(last) + 1]) : [];
    return group.map(row => ({
      task: row,
      groupId: selectedIds.has(row.id) || remaining.length < 2 ? null : row.groupId,
      ...(reorder && selectedIds.has(row.id) ? { position: positions[detached.indexOf(row)] } : {})
    }));
  });
}

export function taskInsertionPosition(rows, task, before = false) {
  const group = taskGroup(rows, task);
  const boundary = (before ? group[0] : group.at(-1)) || task;
  const index = rows.findIndex(row => row.id === boundary.id);
  const neighbor = index < 0 ? null : rows[index + (before ? -1 : 1)];
  return before ? taskDropPositions(1, neighbor, boundary)[0]
    : taskDropPositions(1, boundary, neighbor)[0];
}

export const incompleteTaskGroup = (tasks, rowsFor) => {
  const ids = new Set(tasks.map(task => task.id));
  return tasks.some(task => taskGroup(rowsFor(task), task).some(row => !ids.has(row.id)));
};

export const detachBeforeMove = "Detach the task first, or use the left bracket to move the whole group.";

// A drop from outside a group goes to its top or bottom edge. Only members of
// the same run can use the gaps inside it.
export function taskGroupDrop(rows, target, position, tasks, rowsFor) {
  const group = taskGroup(rows, target);
  const ids = new Set(group.map(row => row.id));
  const withinGroup = tasks.every(task => ids.has(task.id));
  if (!withinGroup && incompleteTaskGroup(tasks, rowsFor)) return null;
  const boundary = withinGroup ? target : (position === "after" ? group.at(-1) : group[0]) || target;
  return { task: boundary, position };
}
