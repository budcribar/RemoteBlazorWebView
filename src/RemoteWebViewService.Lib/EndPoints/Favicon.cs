using Microsoft.AspNetCore.Http;
using System.Reflection;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Favicon()
        {
            return async context =>
            {
                // Specify the resource name, typically it is namespace.filename
                var resourceName = "PeakSWC.RemoteWebView.Resources.favicon.ico";

                // Get the assembly where the resource is embedded
                var assembly = Assembly.GetExecutingAssembly();

                // Set the correct content type for favicon.ico
                context.Response.ContentType = "image/x-icon";

                // Find and stream the embedded file
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Favicon not found").ConfigureAwait(false);
                    return;
                }

                context.Response.ContentLength = stream.Length;
                context.Response.Headers.CacheControl = "public,max-age=604800"; // Cache for 7 days

                await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
            };
        }
    }
}
