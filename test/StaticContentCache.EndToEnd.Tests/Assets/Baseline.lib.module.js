export function beforeStart() {
  window.__moduleInitBaseline = window.__moduleInitBaseline || { beforeStart: 0, afterStarted: 0 };
  window.__moduleInitBaseline.beforeStart += 1;
}

export function afterStarted() {
  window.__moduleInitBaseline = window.__moduleInitBaseline || { beforeStart: 0, afterStarted: 0 };
  window.__moduleInitBaseline.afterStarted += 1;
}
