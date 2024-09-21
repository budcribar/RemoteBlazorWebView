using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSWC.RemoteWebView.Pages;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Contact()
        {
            return async context =>
            {
                // Get the version of the currently executing assembly
                var assembly = Assembly.GetExecutingAssembly();
                var assemblyVersion = assembly.GetName().Version?.ToString() ?? "Version not found";

                // Create the version string
                string versionString = $"Version {assemblyVersion}";

                var contact = new ContactInfo { Company = "Peak Software Consulting, LLC", Email = "budcribar@msn.com", Name = "Bud Cribar", Url = "https://github.com/budcribar/RemoteBlazorWebView" };
                var html = HtmlPageGenerator.GenerateContactPage(contact, versionString);

                context.Response.ContentType = "text/html";

                // Write the version string to the response
                await context.Response.WriteAsync(html).ConfigureAwait(false);
            };
        }

    }
}
