export const marker = "record";

function push(hook, extra) {
  const log = (window.__moduleInitLog ||= []);
  log.push(Object.assign({
    module: new URL(import.meta.url).pathname,
    hook,
    marker
  }, extra));
}

export function beforeStart() {
  push("beforeStart", { argc: arguments.length });
}

export function afterStarted(blazor) {
  push("afterStarted", {
    argc: arguments.length,
    keys: blazor && typeof blazor === "object" ? Object.keys(blazor).sort() : []
  });
}
