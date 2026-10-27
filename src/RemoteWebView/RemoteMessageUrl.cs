using System;

namespace PeakSWC.RemoteWebView
{
    public static class RemoteMessageUrl
    {
        public static Uri Rewrite(string? sender, Uri? serverUri, Guid id, string hostHtmlPath, Uri? localUrl)
        {
            var url = sender ?? "";
            if (serverUri != null && url.StartsWith(serverUri.ToString()))
            {
                url = url.Replace(serverUri.ToString(), localUrl?.ToString() ?? "");
                url = url.Replace(id.ToString() + "/", "");
                if (url.EndsWith(hostHtmlPath))
                    url = url.Replace(hostHtmlPath, "");
            }

            return new Uri(url);
        }
    }
}
