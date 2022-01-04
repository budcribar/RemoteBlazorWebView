﻿using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using Channel = System.Threading.Channels.Channel;

namespace PeakSWC.RemoteWebView
{
    public class Startup
    {
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; } = new();
        private readonly ConcurrentDictionary<string, Channel<string>> serviceStateChannel = new();

#if AUTHORIZATION
        private readonly IConfiguration Configuration;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }


        private async Task<ProtectedApiCallHelper> CreateApiHelper()
        {
            IConfidentialClientApplication confidentialClientApplication =
               ConfidentialClientApplicationBuilder
               .Create(Configuration.GetValue<string>("AzureAdB2C:ClientId"))
               .WithTenantId(Configuration.GetValue<string>("AzureAdB2C:DirectoryId")) 
               .WithClientSecret(Configuration.GetValue<string>("Secret"))
               .Build();

            string[] scopes = new string[] { "https://graph.microsoft.com/.default" };
            AuthenticationResult result = await confidentialClientApplication.AcquireTokenForClient(scopes).ExecuteAsync();
            var httpClient = new HttpClient();
            return new ProtectedApiCallHelper(httpClient,result.AccessToken);
        }
#endif

            public void ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression(options => { options.MimeTypes.Concat(new[] { "application/octet-stream", "application/wasm" }); });
#if AUTHORIZATION
            services.AddTransient((sp) => CreateApiHelper());
            services.AddTransient<IUserService,UserService>();
            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
                // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
                options.HandleSameSiteCookieCompatibility();
            });
            // Configuration to sign-in users with Azure AD B2C
            services.AddMicrosoftIdentityWebAppAuthentication(Configuration, "AzureAdB2C");
            services.AddControllersWithViews().AddMicrosoftIdentityUI();
            services.AddRazorPages();

            //Configuring appsettings section AzureAdB2C, into IOptions
            services.AddOptions();
            services.Configure<OpenIdConnectOptions>(Configuration.GetSection("AzureAdB2C"));
            services.AddAuthorization();


#else
            services.AddTransient<IUserService, MockUserService>();
#endif 
            services.AddSingleton<ConcurrentBag<ServiceState>>();
            services.AddSingleton(ServiceDictionary);
            services.AddSingleton(serviceStateChannel);
            services.AddSingleton<ShutdownService>();

            services.AddGrpc(options => { options.EnableDetailedErrors = true; options.ResponseCompressionLevel = CompressionLevel.Optimal; options.ResponseCompressionAlgorithm = "gzip"; });
            services.AddTransient<RemoteFileResolver>();

            services.AddCors(o =>
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
                    builder.WithExposedHeaders("Grpc-Status", "Grpc-Message","Grpc-Encoding", "Grpc-Accept-Encoding", "X-Grpc-Web", "User-Agent");
                });
            });
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                // TODO
                //app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                //app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseResponseCompression();
            app.UseRouting();

            // Must be between UseRouting() and UseEndPoints()
            app.UseCors("CorPolicy");

            app.UseCookiePolicy();
#if AUTHORIZATION
            app.UseAuthentication();
            app.UseAuthorization();
#endif
            app.UseGrpcWeb();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles(new StaticFileOptions { FileProvider = app.ApplicationServices?.GetService<RemoteFileResolver>() });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<RemoteWebViewService>().AllowAnonymous();
                endpoints.MapGrpcService<ClientIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGet("/app/{id:guid}", Start())
#if AUTHORIZATION     
                .RequireAuthorization()
#endif
                ;
                // Refresh from home page i.e. https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/
                endpoints.MapGet("/{id:guid}", Restart())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
                ;
                // Refresh from nested page i.e.https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
                endpoints.MapGet("/{id:guid}/{unused:alpha}", Restart())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
                ;
                endpoints.MapGet("/wait/{id:guid}", Wait())
#if AUTHORIZATION
                .RequireAuthorization()
#endif
                ;
                endpoints.MapFallbackToFile("index.html");
            });
        }
        private RequestDelegate Start()
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
                        var home = serviceState.HtmlHostPath;
                        if (serviceState.IPC.ClientResponseStream != null)
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" });
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}");
                        context.Response.Redirect($"/{guid}/{home}");
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

        private RequestDelegate Wait()
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

        private RequestDelegate Restart()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                if (ServiceDictionary.TryGetValue(guid, out var serviceState))
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
                else
                {
                    context.Response.StatusCode = 400;                  
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid,true));
                }
            };
        }
    }
}
