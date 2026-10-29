export function beforeStart() {
  (window.__initializerOrder = window.__initializerOrder || []).push("fast.beforeStart");
}

export function afterStarted() {
  window.__initializerOrder.push("fast.afterStarted");
}
