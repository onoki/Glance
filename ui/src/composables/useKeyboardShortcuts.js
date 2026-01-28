export const useKeyboardShortcuts = (options) => {
  const activeTab = options?.activeTab;
  const searchInputRef = options?.searchInputRef;
  const dashboardColumnsRef = options?.dashboardColumnsRef;

  const handleGlobalShortcut = (event) => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "f") {
      const target = event.target;
      const tag = target?.tagName?.toLowerCase();
      if (tag === "input" || tag === "textarea" || target?.isContentEditable) {
        return;
      }
      if (target?.closest?.(".ProseMirror")) {
        return;
      }
      event.preventDefault();
      activeTab.value = "Search";
      queueMicrotask(() => {
        searchInputRef.value?.focus();
      });
    }
  };

  const handleDashboardWheel = (event) => {
    const container = dashboardColumnsRef.value;
    if (!container || activeTab.value !== "Dashboard") {
      return;
    }
    const target = event.target;
    const path = typeof event.composedPath === "function" ? event.composedPath() : [];
    const isInDashboard = path.includes(container) || target?.closest?.(".dashboard");
    if (!isInDashboard) {
      return;
    }
    const deltaX = typeof event.deltaX === "number" ? event.deltaX : 0;
    const deltaY = typeof event.deltaY === "number" ? event.deltaY : 0;
    const legacyDeltaX = typeof event.wheelDeltaX === "number" ? -event.wheelDeltaX : 0;
    const legacyDeltaY = typeof event.wheelDeltaY === "number" ? -event.wheelDeltaY : 0;
    let horizontalDelta = deltaX !== 0 ? deltaX : (legacyDeltaX !== 0 ? legacyDeltaX : 0);
    if (horizontalDelta === 0 && event.shiftKey) {
      horizontalDelta = deltaY !== 0 ? deltaY : legacyDeltaY;
    }
    if (horizontalDelta === 0) {
      return;
    }
    const maxScrollLeft = container.scrollWidth - container.clientWidth;
    if (maxScrollLeft <= 0) {
      return;
    }
    event.preventDefault();
    container.scrollLeft = Math.min(
      maxScrollLeft,
      Math.max(0, container.scrollLeft + horizontalDelta)
    );
  };

  return {
    handleDashboardWheel,
    handleGlobalShortcut
  };
};
