const handlers = new Set();
let receiverRegistered = false;

const getBridge = () => {
  if (typeof window === "undefined") return null;
  const external = window.external;
  return external && typeof external.sendMessage === "function" ? external : null;
};

const ensureReceiver = () => {
  const bridge = getBridge();
  if (!bridge || receiverRegistered || typeof bridge.receiveMessage !== "function") {
    return bridge;
  }
  bridge.receiveMessage((rawMessage) => {
    const value = rawMessage?.data ?? rawMessage;
    let message;
    try {
      message = typeof value === "string" ? JSON.parse(value) : value;
    } catch {
      return;
    }
    handlers.forEach((handler) => {
      try {
        handler(message);
      } catch {
        // One feature must not prevent other desktop message subscribers from running.
      }
    });
  });
  receiverRegistered = true;
  return bridge;
};

export const isDesktopBridgeAvailable = () => !!getBridge();

export const sendDesktopMessage = (message) => {
  const bridge = ensureReceiver();
  if (!bridge) return false;
  bridge.sendMessage(typeof message === "string" ? message : JSON.stringify(message));
  return true;
};

export const registerDesktopMessageHandler = (handler) => {
  if (typeof handler !== "function") return () => {};
  handlers.add(handler);
  ensureReceiver();
  return () => handlers.delete(handler);
};

