function push(hook) {
  const log = (window.__moduleInitLog ||= []);
  log.push({
    module: new URL(import.meta.url).pathname,
    hook,
    marker: "throw"
  });
}

export function beforeStart() {
  push("beforeStart");
  throw new Error("initializer failed");
}

export function afterStarted() {
  push("afterStarted");
}
