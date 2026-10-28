// Test-only hook. Reads nothing from product code. The host page loads this
// before blazor.webview.js so the real boot can finish without a WebView2 host.
(function () {
  var probe = {
    ipc: [],
    bootError: null,
    unhandled: []
  };
  window.__moduleInitializerProbe = probe;
  window.external = {
    sendMessage: function (message) {
      probe.ipc.push(String(message));
    },
    receiveMessage: function () {}
  };
  window.addEventListener("unhandledrejection", function (event) {
    var reason = event.reason;
    var message = reason && reason.message ? String(reason.message) : String(reason);
    probe.bootError = message;
    probe.unhandled.push(message);
  });
})();
