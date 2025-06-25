using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Pages;
using System;
using System.Reflection;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Contact()
        {
            return async context =>
            {
                try
                {
                    // Get the version of the currently executing assembly
                    var assembly = Assembly.GetExecutingAssembly();
                    var assemblyVersion = assembly.GetName().Version?.ToString() ?? "Version not found";

                    // Create the version string
                    string versionString = $"Version {assemblyVersion}";

                    var contact = new ContactInfo
                    {
                        Company = "Peak Software Consulting, LLC",
                        Email = "budcribar@msn.com",
                        Name = "Bud Cribar",
                        Url = "https://github.com/budcribar/RemoteBlazorWebView"
                    };

                    var html = HtmlPageGenerator.GenerateContactPage(contact, versionString);

                    context.Response.ContentType = "text/html";

                    context.Response.ContentLength = html.Length;

                    await context.Response.WriteAsync(html).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ILogger<RemoteWebViewService> logger = context.RequestServices.GetRequiredService<ILogger<RemoteWebViewService>>();
                    logger.LogError(ex, ex.Message);
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("An error occurred while generating the contact page.").ConfigureAwait(false);
                }
            };
        }
    }
}
