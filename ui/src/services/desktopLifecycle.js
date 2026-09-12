import { flushAllSaves, getSaveSummary } from "./saveCoordinator.js";
import {
  isDesktopBridgeAvailable,
  registerDesktopMessageHandler,
  sendDesktopMessage
} from "../utils/desktopBridge.js";

let installed = false;
let removeDesktopHandler = null;
let nextRequestId = 1;
const pending = new Map();

export class InteractionQuiescence {
  constructor(documentRef = typeof document === "undefined" ? null : document) {
    this.document = documentRef;
    this.requests = new Set();
    this.overlay = null;
    this.previouslyInert = false;
  }

  get locked() {
    return this.requests.size > 0;
  }

  lock(requestId, reason = "desktop-action") {
    if (!requestId || this.requests.has(requestId)) return;
    const wasLocked = this.locked;
    this.requests.add(requestId);
    if (wasLocked || !this.document?.body) return;

    this.previouslyInert = !!this.document.body.inert;
    this.document.body.inert = true;
    this.document.documentElement?.setAttribute?.("aria-busy", "true");
    const overlay = this.document.createElement?.("div");
    if (!overlay) return;
    overlay.dataset.glanceInteractionLock = "true";
    overlay.setAttribute("role", "status");
    overlay.setAttribute("aria-live", "polite");
    overlay.textContent = reason === "close" || reason === "restore"
      ? "Saving notes before closing Glance..."
      : "Saving notes...";
    Object.assign(overlay.style, {
      position: "fixed",
      inset: "0",
      zIndex: "2147483647",
      display: "grid",
      placeItems: "center",
      background: "rgba(255, 255, 255, 0.58)",
      color: "#222",
      font: "inherit",
      cursor: "wait"
    });
    this.document.body.appendChild(overlay);
    this.overlay = overlay;
  }

  release(requestId) {
    if (requestId) this.requests.delete(requestId);
    if (this.locked) return;
    if (this.document?.body) this.document.body.inert = this.previouslyInert;
    this.document?.documentElement?.removeAttribute?.("aria-busy");
    this.overlay?.remove?.();
    this.overlay = null;
  }
}

export const interactionQuiescence = new InteractionQuiescence();

const blockKeyboardWhileQuiescent = (event) => {
  if (!interactionQuiescence.locked) return;
  event.preventDefault();
  event.stopImmediatePropagation();
};

const describeFailures = (failures) => {
  const messages = (failures || [])
    .map((failure) => failure.error?.message)
    .filter(Boolean);
  return messages[0] || "One or more notes could not be saved. The window will remain open.";
};

const respondToFlushRequest = async (message, close) => {
  let result;
  try {
    result = await flushAllSaves({ reason: message.reason || (close ? "close" : "desktop-action") });
  } catch (error) {
    result = { ok: false, failures: [{ error }] };
  }
  sendDesktopMessage({
    type: close ? (result.ok ? "closeReady" : "closeBlocked") : "flushResult",
    requestId: message.requestId,
    ok: result.ok,
    message: result.ok ? null : describeFailures(result.failures)
  });
};

const handleDesktopMessage = (message) => {
  if (!message || typeof message !== "object") return;
  if (message.type === "prepareClose" && message.requestId) {
    interactionQuiescence.lock(message.requestId, message.reason || "close");
    void respondToFlushRequest(message, true);
    return;
  }
  if (message.type === "prepareFlush" && message.requestId) {
    interactionQuiescence.lock(message.requestId, message.reason || "desktop-action");
    void respondToFlushRequest(message, false);
    return;
  }
  if (message.type === "operationReleased" && message.requestId) {
    interactionQuiescence.release(message.requestId);
    return;
  }
  if (message.type === "closeCancelled") {
    window.alert(message.message || "Glance stayed open because one or more notes could not be saved.");
    return;
  }
  if (!message.requestId) return;
  const request = pending.get(message.requestId);
  if (!request || !request.responseTypes.has(message.type)) return;
  pending.delete(message.requestId);
  clearTimeout(request.timer);
  if (message.ok === false) request.reject(new Error(message.message || "The desktop action failed."));
  else request.resolve(message);
};

const beforeUnload = (event) => {
  const summary = getSaveSummary();
  if (!summary.dirty && !summary.saving && !summary.failures.length) return;
  event.preventDefault();
  event.returnValue = "Glance is still saving notes.";
};

export const installDesktopLifecycle = () => {
  if (installed) return () => {};
  installed = true;
  removeDesktopHandler = registerDesktopMessageHandler(handleDesktopMessage);
  window.addEventListener("beforeunload", beforeUnload);
  window.addEventListener("keydown", blockKeyboardWhileQuiescent, true);
  return () => {
    removeDesktopHandler?.();
    removeDesktopHandler = null;
    window.removeEventListener("beforeunload", beforeUnload);
    window.removeEventListener("keydown", blockKeyboardWhileQuiescent, true);
    for (const requestId of [...interactionQuiescence.requests]) {
      interactionQuiescence.release(requestId);
    }
    installed = false;
  };
};

const desktopRequest = (type, payload, responseTypes, timeoutMs = 30000) => new Promise((resolve, reject) => {
  installDesktopLifecycle();
  const requestId = `${type}-${Date.now()}-${nextRequestId++}`;
  const timer = setTimeout(() => {
    pending.delete(requestId);
    reject(new Error("Glance did not receive a response from the desktop window."));
  }, timeoutMs);
  pending.set(requestId, { resolve, reject, timer, responseTypes: new Set(responseTypes) });
  try {
    if (!sendDesktopMessage({ type, requestId, ...payload })) {
      throw new Error("This action requires the Glance desktop app.");
    }
  } catch (error) {
    pending.delete(requestId);
    clearTimeout(timer);
    reject(error);
  }
});

export const prepareAllWindows = async (reason) => {
  if (!isDesktopBridgeAvailable()) {
    const result = await flushAllSaves({ reason });
    if (!result.ok) throw new Error(describeFailures(result.failures));
    return true;
  }
  await desktopRequest("coordinateFlush", { reason }, ["coordinateFlushResult"], 60000);
  return true;
};

export const openNewGlanceWindow = () => {
  installDesktopLifecycle();
  if (sendDesktopMessage({ type: "newWindow" })) return true;
  const opened = window.open(window.location.href, "_blank", "noopener,noreferrer");
  return !!opened;
};

export const pickBackupFolder = async (currentPath = null) => {
  if (!isDesktopBridgeAvailable()) return null;
  const response = await desktopRequest(
    "pickFolder",
    { currentPath },
    ["pickFolderResult"]
  );
  return response.path || null;
};

export const restartForPendingRestore = async () => {
  if (!isDesktopBridgeAvailable()) return false;
  try {
    await desktopRequest(
      "restartForRestore",
      {},
      ["restartForRestoreResult"],
      10000
    );
    return true;
  } catch {
    return false;
  }
};
