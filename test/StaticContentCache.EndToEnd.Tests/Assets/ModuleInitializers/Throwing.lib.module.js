export function beforeStart() {
  (window.__initializerOrder = window.__initializerOrder || []).push("throw.beforeStart");
  throw new Error("throwing initializer failed");
}

export function afterStarted() {
  window.__initializerOrder.push("throw.afterStarted");
}
