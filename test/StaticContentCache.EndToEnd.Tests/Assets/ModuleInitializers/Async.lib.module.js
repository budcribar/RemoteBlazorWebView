export async function beforeStart() {
  const log = (window.__initializerOrder = window.__initializerOrder || []);
  log.push("async.beforeStart:start");
  await new Promise((resolve) => setTimeout(resolve, 50));
  log.push("async.beforeStart:end");
}

export async function afterStarted() {
  const log = window.__initializerOrder;
  log.push("async.afterStarted:start");
  await new Promise((resolve) => setTimeout(resolve, 50));
  log.push("async.afterStarted:end");
}
