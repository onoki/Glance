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
    if (!target?.closest?.(".dashboard")) {
      return;
    }
    const deltaX = event.deltaX || 0;
    const deltaY = event.deltaY || 0;
    const horizontalDelta = deltaX !== 0 ? deltaX : (event.shiftKey ? deltaY : 0);
    if (horizontalDelta === 0) {
      return;
    }
    event.preventDefault();
    container.scrollLeft += horizontalDelta;
  };

  return {
    handleDashboardWheel,
    handleGlobalShortcut
  };
};
