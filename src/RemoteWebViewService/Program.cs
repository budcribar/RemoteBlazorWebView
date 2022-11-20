using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
//using Wasi.AspNetCore.Server.Native;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.IO.Compression;

namespace PeakSWC.RemoteWebView
{
    public class Program
    {
        private static ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; } = new();
        private static readonly ConcurrentDictionary<string, Channel<string>> serviceStateChannel = new();
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);//.UseWasiConnectionListener();


            builder.Services.AddResponseCompression(options => { options.MimeTypes.Concat(new[] { "application/octet-stream", "application/wasm" }); });
#if AUTHORIZATION
          
            builder.Services.AddTransient((sp) => CreateApiHelper(builder.Configuration));
            builder.Services.AddTransient<IUserService, UserService>();
            builder.Services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
                // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
                options.HandleSameSiteCookieCompatibility();
            });
            // Configuration to sign-in users with Azure AD B2C
            builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAdB2C");
            builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
            builder.Services.AddRazorPages();

            //Configuring appsettings section AzureAdB2C, into IOptions
            builder.Services.AddOptions();
            builder.Services.Configure<OpenIdConnectOptions>(builder.Configuration.GetSection("AzureAdB2C"));
            builder.Services.AddAuthorization();


#else
            builder.Services.AddTransient<IUserService, MockUserService>();
#endif 
            builder.Services.AddSingleton(ServiceDictionary);
            builder.Services.AddSingleton(serviceStateChannel);
            builder.Services.AddSingleton<ShutdownService>();

            builder.Services.AddGrpc(options => { options.EnableDetailedErrors = true; options.ResponseCompressionLevel = CompressionLevel.Optimal; options.ResponseCompressionAlgorithm = "gzip"; });
            builder.Services.AddTransient<RemoteFileResolver>();

            builder.Services.AddCors(o =>
            {
                o.AddPolicy("CorsPolicy", builder =>
                {
                    builder.AllowAnyOrigin();
                    builder.AllowAnyHeader();
                    builder.AllowAnyMethod();

                    // TODO tighten this up
                    //builder.WithOrigins("localhost:443", "localhost", "YourCustomDomain");
                    // builder.WithMethods("POST, OPTIONS");
                    //builder.AllowAnyHeader();
                    builder.WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding", "X-Grpc-Web", "User-Agent");
                });
            });
            var app = builder.Build();
            app.UseHttpsRedirection();
            app.UseResponseCompression();
            app.UseRouting();

            app.UseCors("CorPolicy");

            app.UseCookiePolicy();
#if AUTHORIZATION
            app.UseAuthentication();
            app.UseAuthorization();
#endif
            app.UseGrpcWeb();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles(new StaticFileOptions { FileProvider = app.Services?.GetService<RemoteFileResolver>() });


            app.MapGrpcService<RemoteWebViewService>().AllowAnonymous();
            app.MapGrpcService<ClientIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
            app.MapGrpcService<BrowserIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");

            app.MapGet("/mirror/{id:guid}", Mirror())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
            ;
            app.MapGet("/app/{id:guid}", Start())
#if AUTHORIZATION     
                .RequireAuthorization()
#endif
                ;
            // Refresh from home page i.e. https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/
            app.MapGet("/{id:guid}", StartOrRefresh())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
                ;
            // Refresh from nested page i.e.https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
            app.MapGet("/{id:guid}/{unused:alpha}", StartOrRefresh())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
                ;
            app.MapGet("/wait/{id:guid}", Wait())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
                ;
            app.MapFallbackToFile("index.html");


            app.Run();

            //Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
            //CreateHostBuilder(args).Build().Run();
        }

#if AUTHORIZATION

        private static async Task<ProtectedApiCallHelper> CreateApiHelper(ConfigurationManager configurationManager)
        {
            IConfidentialClientApplication confidentialClientApplication =
               ConfidentialClientApplicationBuilder
               .Create(configurationManager.GetValue<string>("AzureAdB2C:ClientId"))
               .WithTenantId(configurationManager.GetValue<string>("AzureAdB2C:DirectoryId"))
               .WithClientSecret(configurationManager.GetValue<string>("Secret"))
               .Build();

            string[] scopes = new string[] { "https://graph.microsoft.com/.default" };
            AuthenticationResult result = await confidentialClientApplication.AcquireTokenForClient(scopes).ExecuteAsync();
            var httpClient = new HttpClient();
            return new ProtectedApiCallHelper(httpClient, result.AccessToken);
        }
#endif

        private static RequestDelegate Mirror()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                if (ServiceDictionary.TryGetValue(guid, out var serviceState))
                {
                    if (serviceState.EnableMirrors)
                    {
                        serviceState.User = context.User.GetDisplayName() ?? "";

                        if (serviceState.IPC.ClientResponseStream != null)
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" });
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}");

                        var home = serviceState.HtmlHostPath;
                        var rfr = context.RequestServices.GetService<RemoteFileResolver>();
                        var fi = rfr?.GetFileInfo($"/{guid}/{home}");
                        context.Response.ContentLength = fi?.Length;
                        using var stream = fi?.CreateReadStream();
                        if (stream != null)
                        {
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "text/html";

                            await stream.CopyToAsync(context.Response.Body);
                        }
                        else context.Response.StatusCode = 400;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync("Mirroring is not enabled");
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            };

        }

        private static RequestDelegate Start()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                if (ServiceDictionary.TryGetValue(guid, out var serviceState))
                {
                    if (serviceState.InUse)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(LockedPage.Html(serviceState.User, guid));
                    }
                    else
                    {

                        serviceState.InUse = true;
                        serviceState.User = context.User.GetDisplayName() ?? "";

                        if (serviceState.IPC.ClientResponseStream != null)
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" });
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}");

                        context.Response.Redirect($"/{guid}");
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid, false));
                }
            };
        }

        private static RequestDelegate Wait()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                for (int i = 0; i < 30; i++)
                {
                    if (ServiceDictionary.ContainsKey(guid))
                    {
                        await context.Response.WriteAsync($"Wait completed");
                        return;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }

                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(RestartFailedPage.Fragment(guid));
            };
        }

        private static RequestDelegate StartOrRefresh()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                if (ServiceDictionary.TryGetValue(guid, out var serviceState))
                {
                    if (!serviceState.Refresh)
                    {
                        serviceState.Refresh = true;
                        var home = serviceState.HtmlHostPath;
                        var rfr = context.RequestServices.GetService<RemoteFileResolver>();
                        var fi = rfr?.GetFileInfo($"/{guid}/{home}");
                        context.Response.ContentLength = fi?.Length;
                        using var stream = fi?.CreateReadStream();
                        if (stream != null)
                        {
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "text/html";
                            await stream.CopyToAsync(context.Response.Body);
                            //TextReader tr = new StreamReader(stream);
                            //var text = await tr.ReadToEndAsync();
                            //await context.Response.WriteAsync(text);
                        }
                        else context.Response.StatusCode = 400;
                    }
                    else
                    {
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = "refreshed:" });

                        // Wait until client shuts down 
                        for (int i = 0; i < 3000; i++)
                        {
                            if (!ServiceDictionary.ContainsKey(guid))
                            {
                                context.Response.ContentType = "text/html";
                                await context.Response.WriteAsync(RestartPage.Html(guid, serviceState?.ProcessName ?? "", serviceState?.HostName ?? ""));
                                return;
                            }

                            await Task.Delay(10).ConfigureAwait(false);
                        }

                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";

                        await context.Response.WriteAsync(RestartFailedPage.Html(serviceState.ProcessName, serviceState.Pid, serviceState.HostName));

                        // Shutdown since client did not respond to restart request
                        context.RequestServices.GetRequiredService<ShutdownService>().Shutdown(guid);
                        await Task.CompletedTask;
                    }

                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid, true));
                }
            };
        }
    }

    //public static IHostBuilder CreateHostBuilder(string[] args) =>
    //        Host.CreateDefaultBuilder(args).UseWindowsService()
    //            .ConfigureWebHostDefaults(webBuilder =>
    //            {
    //                if (!File.Exists("appsettings.json"))
    //                    webBuilder.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 5001, listenOptions => { listenOptions.UseHttps(); }));

    //                // Comment out for App Service
    //                webBuilder.UseKestrel();

    //                // Uncomment for App Service
    //                // webBuilder.UseIISIntegration();

    //                webBuilder.UseStartup<Startup>();
    //            });
    //}
}
