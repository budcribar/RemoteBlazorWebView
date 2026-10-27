// Test-only loader. Mirrors blazor.webview.js: fetch _framework/blazor.modules.json,
// import new URL(name, document.baseURI), call beforeStart, then afterStarted.
// It reads window.__moduleInitBaseline. It does not ship with the product.
const output = document.getElementById("module-init-hook");

function snapshot() {
  const state = window.__moduleInitBaseline || { beforeStart: 0, afterStarted: 0 };
  return { beforeStart: state.beforeStart || 0, afterStarted: state.afterStarted || 0 };
}

try {
  const manifestUrl = new URL("_framework/blazor.modules.json", document.baseURI);
  const manifestResponse = await fetch(manifestUrl, { method: "GET", credentials: "include", cache: "no-cache" });
  const names = await manifestResponse.json();
  const fetched = [];
  const steps = [];
  for (const name of names) {
    const moduleUrl = new URL(name, document.baseURI);
    fetched.push(moduleUrl.pathname);
    const loaded = await import(moduleUrl.href);
    await loaded.beforeStart();
    steps.push({ hook: "beforeStart", state: snapshot() });
    await loaded.afterStarted();
    steps.push({ hook: "afterStarted", state: snapshot() });
  }
  const report = {
    baseURI: document.baseURI,
    names,
    fetched,
    steps,
    final: snapshot()
  };
  output.textContent = JSON.stringify(report);
  await fetch(new URL("module-init-result?report=" + encodeURIComponent(JSON.stringify(report)), document.baseURI), { cache: "no-store" });
} catch (error) {
  const report = { error: String(error) };
  output.textContent = JSON.stringify(report);
  await fetch(new URL("module-init-result?report=" + encodeURIComponent(JSON.stringify(report)), document.baseURI), { cache: "no-store" });
}
