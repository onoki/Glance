import { isDocEmptyJson } from "./taskDocUtils.js";

export const shouldDeleteEmptyOnComplete = (task) => (
  !task?.completedAt
  && isDocEmptyJson(task.title)
  && isDocEmptyJson(task.content)
);
