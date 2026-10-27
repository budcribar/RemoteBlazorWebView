const moduleUrl = import.meta.url;

export function beforeStart() {
  window.__moduleInitBaseline = window.__moduleInitBaseline || { beforeStart: 0, afterStarted: 0, order: [], url: moduleUrl };
  window.__moduleInitBaseline.url = moduleUrl;
  window.__moduleInitBaseline.beforeStart += 1;
  window.__moduleInitBaseline.order.push("beforeStart");
}

export function afterStarted() {
  window.__moduleInitBaseline = window.__moduleInitBaseline || { beforeStart: 0, afterStarted: 0, order: [], url: moduleUrl };
  window.__moduleInitBaseline.url = moduleUrl;
  window.__moduleInitBaseline.afterStarted += 1;
  window.__moduleInitBaseline.order.push("afterStarted");
}
