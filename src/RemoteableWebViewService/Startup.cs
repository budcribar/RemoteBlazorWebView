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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Channel = System.Threading.Channels.Channel;

namespace PeakSWC.RemoteableWebView
{
    public class Startup
    {
        private readonly ConcurrentDictionary<string, ServiceState> rootDictionary = new();
        private readonly Channel<ClientResponseList> serviceStateChannel = Channel.CreateUnbounded<ClientResponseList>();
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

        public void ConfigureServices(IServiceCollection services)
        {
#if NET5
            services.AddResponseCompression(options => { options.MimeTypes = new[] { "application/octet-stream", "application/wasm" }; });
#else
            services.AddResponseCompression(options => { options.MimeTypes.Concat(new[] { "application/octet-stream", "application/wasm" }); });
#endif

#if AUTHORIZATION
            services.AddSingleton(CreateApiHelper().Result);
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
#endif 
            services.AddSingleton(rootDictionary);
            services.AddSingleton(serviceStateChannel);

            services.AddGrpc(options => { options.EnableDetailedErrors = true; });
            services.AddTransient<FileResolver>();

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

            // TODO Is this needed or does UseBlazorFrameworkFiles take care of it?
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
            app.UseStaticFiles();
            app.UseStaticFiles(new StaticFileOptions { FileProvider = app.ApplicationServices?.GetService<FileResolver>() });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<RemoteWebViewService>().AllowAnonymous();
                endpoints.MapGrpcService<ClientIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");

                endpoints.MapGet("/app/{id:guid}", async context =>
                {
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                    if (rootDictionary.ContainsKey(guid))
                    {
                        if (rootDictionary[guid].InUse)
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsync("Client is currently locked");

                        }
                        else
                        {
                            rootDictionary[guid].InUse = true;
                            // Update Status
                            var list = new ClientResponseList();
                            rootDictionary.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));
                            await serviceStateChannel.Writer.WriteAsync(list);

                            var home = rootDictionary[guid].HtmlHostPath;

                            context.Response.Redirect($"/{guid}/{home}");
                        }

                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid Guid");
                    }
                })
#if AUTHORIZATION     
                .RequireAuthorization()
#endif
                ;
                endpoints.MapGet("/restart/{id:guid}", async context =>
                {
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                    // wait until client shuts down
                    for (int i = 0; i < 30; i++)
                    {
                        if (!rootDictionary.ContainsKey(guid))
                            break;
                        await Task.Delay(1000);
                    }
                    if (rootDictionary.ContainsKey(guid))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync($"Unable to restart -> Client did not shut down");
                        return;
                    }

                    var text = "<script type='text/javascript'>" +
                    "var xmlHttp = new XMLHttpRequest();" +
                    "xmlHttp.onreadystatechange = function () {" +
                    "   if (xmlHttp.readyState == 4 && xmlHttp.status == 200)" +
                    $"     window.location.assign('/app/{guid}');" +
                    "  };" +
                    $" xmlHttp.open('GET', '/wait/{guid}', true);" +
                    "  xmlHttp.send(null);" +
                    "</script><h1 class='display-4'>Restarting...</h1>";

                    await context.Response.WriteAsync(text);

                });

                endpoints.MapGet("/wait/{id:guid}", async context =>
               {
                   string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                   for (int i = 0; i < 30; i++)
                   {
                       if (rootDictionary.ContainsKey(guid))
                           break;
                       await Task.Delay(1000);
                   }
                   if (rootDictionary.ContainsKey(guid))
                       await context.Response.WriteAsync($"Wait completed");
                   else
                   {
                       context.Response.StatusCode = 400;
                       await context.Response.WriteAsync($"Unable to restart -> Timed out");
                   }


               });

                endpoints.MapGet("/{id:guid}", async context =>
                {
                    // Refresh from home page i.e. https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/

                    string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                    if (rootDictionary.ContainsKey(guid))
                    {
                        rootDictionary[guid].IPC.ReceiveMessage(new WebMessageResponse { Response = "booted:" });
                        context.Response.Redirect($"/restart/{guid}");
                        await Task.CompletedTask;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid Guid");
                    }
                });

                endpoints.MapGet("/{id:guid}/{unused:alpha}", async context =>
                {
                    // Refresh from nested page i.e.https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
                    string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                    if (rootDictionary.ContainsKey(guid))
                    {
                        rootDictionary[guid].IPC.ReceiveMessage(new WebMessageResponse { Response = "booted:" });
                        context.Response.Redirect($"/restart/{guid}");
                        await Task.CompletedTask;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid Guid");
                    }

                });

                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}