// Test-only loader. Mirrors blazor.webview.js from the net11 WebView package:
// fetch the manifest with cache "no-cache", import every name with
// new URL(name, document.baseURI) inside Promise.all, call beforeStart with
// no arguments, and call afterStarted(blazor) only after that stage resolves.
const output = document.getElementById("module-init-hook");
const blazor = {
  navigateTo() {},
  registerCustomEventType() {},
  rootComponents: {},
  runtime: {},
  _internal: {}
};

function snapshot() {
  return (window.__moduleInitLog || []).slice();
}

async function publish(report) {
  const json = JSON.stringify(report);
  output.textContent = json;
  await fetch(new URL("module-init-result?report=" + encodeURIComponent(json), document.baseURI), { cache: "no-store" });
}

const load = sessionStorage.getItem("module-init-load") || "1";
const names = [];
let stageError = null;
try {
  const manifestUrl = new URL("_framework/blazor.modules.json", document.baseURI);
  const manifestResponse = await fetch(manifestUrl, { method: "GET", credentials: "include", cache: "no-cache" });
  if (!manifestResponse.ok)
    throw new Error("manifest HTTP " + manifestResponse.status);
  const listed = await manifestResponse.json();
  names.push(...listed);
  const afterStarted = [];
  const results = await Promise.allSettled(listed.map(async (name) => {
    const moduleUrl = new URL(name, document.baseURI);
    const loaded = await import(moduleUrl.href);
    if (typeof loaded.afterStarted === "function")
      afterStarted.push(loaded.afterStarted);
    if (typeof loaded.beforeStart === "function")
      await loaded.beforeStart();
  }));
  const rejected = results.find((result) => result.status === "rejected");
  if (rejected)
    throw rejected.reason;
  const afterResults = await Promise.allSettled(afterStarted.map((hook) => hook(blazor)));
  const afterRejected = afterResults.find((result) => result.status === "rejected");
  if (afterRejected)
    throw afterRejected.reason;
} catch (error) {
  stageError = String(error);
}

await publish({
  baseURI: document.baseURI,
  names,
  stageError,
  load,
  log: snapshot()
});

const params = new URLSearchParams(location.search);
if (load === "1" && params.get("reload") === "1") {
  sessionStorage.setItem("module-init-load", "2");
  location.reload();
}
if (load === "1" && params.get("swap") === "1") {
  const deadline = Date.now() + 14000;
  let swapped = false;
  while (Date.now() < deadline) {
    const probe = await fetch(new URL("module-init-swap.txt", document.baseURI), { cache: "no-store" });
    if (probe.ok && (await probe.text()).trim() === "go") {
      swapped = true;
      break;
    }
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
  if (!swapped) {
    await publish({
      baseURI: document.baseURI,
      names,
      stageError: "Timed out waiting for module-init-swap.txt",
      load: "2",
      log: []
    });
  } else {
    sessionStorage.setItem("module-init-load", "2");
    location.reload();
  }
}
