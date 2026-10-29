window.__initializerArgs = { beforeCount: -1, afterCount: -1, afterIsBlazor: false, afterHasStart: false };

export function beforeStart() {
  window.__initializerArgs.beforeCount = arguments.length;
}

export function afterStarted(blazor) {
  window.__initializerArgs.afterCount = arguments.length;
  window.__initializerArgs.afterIsBlazor = blazor === window.Blazor;
  window.__initializerArgs.afterHasStart = typeof blazor.start === "function";
}
