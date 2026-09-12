import { DASHBOARD_MAIN_PAGE, DASHBOARD_NEW_PAGE } from "./pageConstants.js";

export const resolveSearchDestination = (task) => {
  if (!task?.id) {
    return null;
  }
  if (task.completedAt !== null && task.completedAt !== undefined) {
    return { tab: "History", taskId: task.id };
  }
  if (task.page === "people:main" && task.ownerPersonId) {
    return {
      tab: "People",
      taskId: task.id,
      personId: task.ownerPersonId,
      personName: task.ownerPersonName || "this person"
    };
  }
  if (task.page === DASHBOARD_NEW_PAGE || task.page === DASHBOARD_MAIN_PAGE) {
    return { tab: "Dashboard", taskId: task.id };
  }
  return null;
};

