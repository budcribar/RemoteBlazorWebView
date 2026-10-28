window.__otherModule = window.__otherModule || { beforeStart: 0, afterStarted: 0, order: [] };

export function beforeStart() {
  window.__otherModule.beforeStart += 1;
  window.__otherModule.order.push("beforeStart");
}

export function afterStarted() {
  window.__otherModule.afterStarted += 1;
  window.__otherModule.order.push("afterStarted");
}
