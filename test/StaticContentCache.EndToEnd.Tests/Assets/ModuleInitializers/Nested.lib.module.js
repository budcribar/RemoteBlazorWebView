window.__nestedModule = { beforeStart: 0, afterStarted: 0, order: [], url: import.meta.url };

export function beforeStart() {
  window.__nestedModule.beforeStart += 1;
  window.__nestedModule.order.push("beforeStart");
}

export function afterStarted() {
  window.__nestedModule.afterStarted += 1;
  window.__nestedModule.order.push("afterStarted");
}
