// Baseline JS initializer for remote blazor.modules.json parity.
// beforeStart and afterStarted record that they ran. Keep this file deterministic.
export function beforeStart() {
  window.__moduleInitBaseline = window.__moduleInitBaseline || {};
  window.__moduleInitBaseline.beforeStart = true;
}

export function afterStarted() {
  window.__moduleInitBaseline = window.__moduleInitBaseline || {};
  window.__moduleInitBaseline.afterStarted = true;
}
