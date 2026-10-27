export const marker = "slow";

function push(hook, extra) {
  const log = (window.__moduleInitLog ||= []);
  log.push(Object.assign({
    module: new URL(import.meta.url).pathname,
    hook,
    marker
  }, extra));
}

export async function beforeStart() {
  push("beforeStart", { argc: arguments.length, phase: "enter" });
  await new Promise((resolve) => setTimeout(resolve, 40));
  push("beforeStart", { argc: arguments.length, phase: "leave" });
}

export function afterStarted(blazor) {
  push("afterStarted", {
    argc: arguments.length,
    keys: blazor && typeof blazor === "object" ? Object.keys(blazor).sort() : [],
    phase: "after"
  });
}
