export const millisecondsUntilNextDay = (now = new Date()) => {
  const next = new Date(now);
  next.setHours(24, 0, 0, 0);
  return next.getTime() - now.getTime() + 20;
};

// Midnight timer plus focus/visibility recovery after sleep and clock changes.
export function installDayRollover(check, { host = window, doc = document, now = () => new Date(),
  setTimer = setTimeout, clearTimer = clearTimeout, report = error => console.warn('Day rollover will retry', error) } = {}) {
  let timer = null, running = null, disposed = false;
  const schedule = () => {
    if (timer !== null) clearTimer(timer);
    if (!disposed) timer = setTimer(run, Math.min(60000, Math.max(20, millisecondsUntilNextDay(now()))));
  };
  const run = () => {
    if (disposed) return Promise.resolve();
    if (running) return running;
    running = Promise.resolve().then(check).catch(report).finally(() => { running = null; schedule(); });
    return running;
  };
  const visible = () => { if (doc.visibilityState !== 'hidden') void run(); };
  host.addEventListener('focus', run);
  doc.addEventListener('visibilitychange', visible);
  schedule();
  return () => { disposed = true; if (timer !== null) clearTimer(timer); host.removeEventListener('focus', run); doc.removeEventListener('visibilitychange', visible); };
}
