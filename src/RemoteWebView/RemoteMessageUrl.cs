using System;

namespace PeakSWC.RemoteWebView
{
    public static class RemoteMessageUrl
    {
        public static Uri Rewrite(string? sender, Uri? serverUri, Guid id, string hostHtmlPath, Uri? localUrl)
        {
            var url = sender ?? "";
            if (serverUri != null && url.StartsWith(serverUri.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var serverPrefix = serverUri.ToString();
                url = (localUrl?.ToString() ?? "") + url[serverPrefix.Length..];
                var idSegment = id.ToString() + "/";
                var idx = url.IndexOf(idSegment, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    url = url[..idx] + url[(idx + idSegment.Length)..];
                }
                if (!string.IsNullOrEmpty(hostHtmlPath))
                {
                    url = url.Replace(hostHtmlPath, "", StringComparison.OrdinalIgnoreCase);
                }
            }

            return new Uri(url);
        }
    }
}
