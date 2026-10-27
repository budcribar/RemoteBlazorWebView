export function beforeStart() {
  (window.__initializerOrder = window.__initializerOrder || []).push("second.beforeStart");
}

export function afterStarted() {
  window.__initializerOrder.push("second.afterStarted");
}
