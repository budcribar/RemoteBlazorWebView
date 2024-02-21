﻿
using System.Diagnostics.CodeAnalysis;

namespace PeakSWC.RemoteWebView.Pages
{
    public static class LockedPage
    {
        public static string Html(string user, string guid)
        {
            if (string.IsNullOrEmpty(user))
                user = string.Empty;
            else
                user = "to: <b>" + user + "</b>";
            // <auto-generated>
            string html = $"""
                <!DOCTYPE html>
                <html lang = 'en' style='height: 100%;' >
                <head>
                  <title>Locked</title>
                  <meta charset='utf-8'>
                  <meta name='viewport' content='width=device-width, initial-scale=1'>
                </head>
                <body style='margin:0;width:100%; height:100%;'>

                <div style='vertical-align:middle;margin:0;height:100%;color:#a94442;background-color:#f2dede;text-align:center;'>
                <img style = 'height:200px;display:block; margin-left: auto;margin-right: auto;' src='data:image/png;base64, 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'/>

                <span style='text-align:center;'>Locked {user}</span>
                <button type='button' onclick="location.href='/{guid}'">Restart</button>
                </div>
                </body>
                </html>
            """;
            // </auto-generated>
            return html;
        }
    }
}
