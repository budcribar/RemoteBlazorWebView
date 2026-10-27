export function beforeStart() {
  (window.__initializerOrder = window.__initializerOrder || []).push("sibling.beforeStart");
}

export function afterStarted() {
  window.__initializerOrder.push("sibling.afterStarted");
}
