const version = 1;

export function beforeStart() {
  window.__changedModule = { version: version, beforeStart: 1, afterStarted: 0, order: ["beforeStart"] };
}

export function afterStarted() {
  window.__changedModule.afterStarted += 1;
  window.__changedModule.order.push("afterStarted");
}
