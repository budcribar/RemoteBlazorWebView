using Microsoft.AspNetCore.Builder;
using System;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles
{
    public static class RemoteFilesMiddlewareExtensions
    {
        public static IApplicationBuilder UseRemoteFiles(this IApplicationBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            return builder.UseMiddleware<RemoteFilesMiddleware>();
        }
    }

}
