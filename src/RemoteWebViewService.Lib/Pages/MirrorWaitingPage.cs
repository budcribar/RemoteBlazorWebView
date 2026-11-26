namespace PeakSWC.RemoteWebView.Pages
{
    /// <summary>
    /// Shown on /mirror/{id} while the app is registered but its own page is not open yet, typically just after the PC
    /// restarted. The page loads itself again every few seconds until the mirror is served. It is sent with 503, so the
    /// app page's own "come back" checks (which wait for a successful answer) keep waiting too.
    /// </summary>
    public static class MirrorWaitingPage
    {
        public const int RetrySeconds = 3;

        public static string Html(string processName, string hostName)
        {
            var safeProcess = System.Net.WebUtility.HtmlEncode(processName ?? string.Empty);
            var safeHost = System.Net.WebUtility.HtmlEncode(hostName ?? string.Empty);
            string waitingFor = string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(hostName)
                ? "Waiting for the app to open on the PC"
                : $"Waiting for <b>{safeProcess}</b> to open on <b>{safeHost}</b>";

            return $$"""
                <!DOCTYPE html>
                <html lang='en' style='height: 100%;'>
                <head>
                  <title>Waiting...</title>
                  <meta charset='utf-8'>
                  <meta name='viewport' content='width=device-width, initial-scale=1'>
                  <meta http-equiv='refresh' content='{{RetrySeconds}}'>
                </head>
                <body style='margin:0; width:100%; height:100%; background-color:#f1f2f3; font-family:sans-serif;'>
                  <div id='waiting' role='status' style='padding-top:20vh; text-align:center; color:#333;'>{{waitingFor}}&hellip;<br><small>This page will connect by itself.</small></div>
                </body>
                </html>
                """;
        }
    }
}
