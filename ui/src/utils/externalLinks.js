import {
  isDesktopBridgeAvailable,
  registerDesktopMessageHandler,
  sendDesktopMessage
} from "./desktopBridge.js";

const WEB_SCHEMES = new Set(["http:", "https:"]);
const ALLOWED_SCHEMES = new Set(["http:", "https:", "mailto:", "file:"]);

const BLOCKED_FILE_EXTENSIONS = new Set([
  ".appref-ms",
  ".bat",
  ".cmd",
  ".com",
  ".cpl",
  ".exe",
  ".hta",
  ".inf",
  ".ins",
  ".isp",
  ".js",
  ".jse",
  ".lnk",
  ".msh",
  ".msh1",
  ".msh2",
  ".mshxml",
  ".msi",
  ".msp",
  ".mst",
  ".pif",
  ".ps1",
  ".ps1xml",
  ".ps2",
  ".ps2xml",
  ".psc1",
  ".psc2",
  ".reg",
  ".scf",
  ".scr",
  ".sct",
  ".url",
  ".vb",
  ".vbe",
  ".vbs",
  ".ws",
  ".wsc",
  ".wsf",
  ".wsh"
]);

const WINDOWS_DRIVE_PATH = /^[a-zA-Z]:[\\/]/;
const WINDOWS_UNC_PATH = /^\\\\[^\\]/;
const EXPLICIT_SCHEME = /^[a-zA-Z][a-zA-Z\d+.-]*:/;
const BARE_DOMAIN = /^(?:[a-z\d](?:[a-z\d-]*[a-z\d])?\.)+[a-z]{2,}(?::\d+)?(?:[/?#].*)?$/i;

const hasControlCharacters = (value) => Array.from(value)
  .some((character) => character.charCodeAt(0) <= 31 || character.charCodeAt(0) === 127);

const encodePathParts = (parts) => parts.map((part) => encodeURIComponent(part)).join("/");

const windowsPathToFileUri = (value) => {
  if (WINDOWS_DRIVE_PATH.test(value)) {
    const drive = value[0].toUpperCase();
    const remainder = value.slice(2).replace(/^[\\/]+/, "");
    const parts = remainder ? remainder.split(/[\\/]+/) : [];
    return `file:///${drive}:/${encodePathParts(parts)}`;
  }

  if (WINDOWS_UNC_PATH.test(value)) {
    if (value.startsWith("\\\\?\\") || value.startsWith("\\\\.\\")) {
      return null;
    }
    const parts = value.slice(2).split(/[\\/]+/);
    const host = parts.shift();
    const share = parts.shift();
    if (!host || !share || /[^a-zA-Z\d._-]/.test(host)) {
      return null;
    }
    return `file://${host}/${encodePathParts([share, ...parts])}`;
  }

  return null;
};

const getFileExtension = (url) => {
  let pathname;
  try {
    pathname = decodeURIComponent(url.pathname);
  } catch {
    return null;
  }
  const finalSegment = pathname.replace(/[\\/]+$/, "").split(/[\\/]/).pop() || "";
  const dot = finalSegment.lastIndexOf(".");
  return dot >= 0 ? finalSegment.slice(dot).toLowerCase() : "";
};

const parseAllowedUri = (value) => {
  let parsed;
  try {
    parsed = new URL(value);
  } catch {
    return null;
  }
  if (!ALLOWED_SCHEMES.has(parsed.protocol)) {
    return null;
  }
  if (WEB_SCHEMES.has(parsed.protocol) && !parsed.hostname) {
    return null;
  }
  if (parsed.protocol === "mailto:" && !parsed.pathname) {
    return null;
  }
  if (parsed.protocol === "file:") {
    if (parsed.username || parsed.password || parsed.search || parsed.hash) {
      return null;
    }
    if (!parsed.hostname && !/^\/[a-zA-Z]:\//.test(parsed.pathname)) {
      return null;
    }
    if (parsed.hostname === "." || parsed.hostname === "?") {
      return null;
    }
    const extension = getFileExtension(parsed);
    if (extension === null || BLOCKED_FILE_EXTENSIONS.has(extension)) {
      return null;
    }
  }
  return parsed;
};

export const normalizeExternalTarget = (input) => {
  if (typeof input !== "string") {
    return null;
  }
  const value = input.trim();
  if (!value || hasControlCharacters(value)) {
    return null;
  }

  const fileUri = windowsPathToFileUri(value);
  if (fileUri) {
    return parseAllowedUri(fileUri)?.href || null;
  }

  let candidate = value;
  if (/^www\./i.test(candidate) || (!EXPLICIT_SCHEME.test(candidate) && BARE_DOMAIN.test(candidate))) {
    candidate = `https://${candidate}`;
  }
  return parseAllowedUri(candidate)?.href || null;
};

export const isAllowedExternalTarget = (input) => normalizeExternalTarget(input) !== null;

let externalReceiverRegistered = false;
let nextRequestId = 1;
const pendingRequests = new Map();

const ensureDesktopReceiver = () => {
  if (externalReceiverRegistered) {
    return;
  }
  registerDesktopMessageHandler((message) => {
    if (message?.type !== "openExternalResult" || !message.requestId) {
      return;
    }
    const pending = pendingRequests.get(message.requestId);
    if (!pending) {
      return;
    }
    pendingRequests.delete(message.requestId);
    clearTimeout(pending.timer);
    if (message.ok) {
      pending.resolve(true);
    } else {
      pending.reject(new Error(message.message || "Windows could not open the link."));
    }
  });
  externalReceiverRegistered = true;
};

const openThroughDesktop = (target) => new Promise((resolve, reject) => {
  ensureDesktopReceiver();
  const requestId = `open-${Date.now()}-${nextRequestId++}`;
  const timer = setTimeout(() => {
    pendingRequests.delete(requestId);
    reject(new Error("Glance did not receive a response while opening the link."));
  }, 10000);
  pendingRequests.set(requestId, { resolve, reject, timer });
  try {
    if (!sendDesktopMessage({ type: "openExternal", requestId, target })) {
      throw new Error("The Glance desktop bridge is unavailable.");
    }
  } catch (error) {
    pendingRequests.delete(requestId);
    clearTimeout(timer);
    reject(error);
  }
});

export const openExternalTarget = async (input) => {
  const target = normalizeExternalTarget(input);
  if (!target) {
    throw new Error("This link is not allowed. Use an http(s), mail, mapped-drive, UNC, or file link to a non-executable file.");
  }

  if (isDesktopBridgeAvailable()) {
    return openThroughDesktop(target);
  }

  const protocol = new URL(target).protocol;
  if (!WEB_SCHEMES.has(protocol) && protocol !== "mailto:") {
    throw new Error("File and network-drive links can only be opened from the Glance desktop app.");
  }
  const opened = window.open(target, "_blank", "noopener,noreferrer");
  if (opened) {
    opened.opener = null;
  }
  return true;
};
