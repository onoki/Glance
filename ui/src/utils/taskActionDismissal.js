// Window activation can restore DOM focus before the returning click arrives.
// Keep controls out of hit testing until a task receives the completed click.
// Do not call interact from focusin/pointerdown: that could change its target.
export function createTaskActionDismissal(setDismissed) {
  let dismissed = false;
  return {
    blur() {
      dismissed = true;
      setDismissed(true);
    },
    interact() {
      if (!dismissed) return;
      // A deliberate keystroke also resumes editing after keyboard activation.
      dismissed = false;
      setDismissed(false);
    }
  };
}
