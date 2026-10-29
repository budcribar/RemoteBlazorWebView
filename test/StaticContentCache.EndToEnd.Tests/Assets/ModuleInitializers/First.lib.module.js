export function beforeStart() {
  (window.__initializerOrder = window.__initializerOrder || []).push("first.beforeStart");
}

export function afterStarted() {
  window.__initializerOrder.push("first.afterStarted");
}
