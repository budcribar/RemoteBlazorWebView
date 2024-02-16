
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
#if AUTHORIZATION
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using System.Net.Http;
using Microsoft.Identity.Web.UI;
#endif

namespace PeakSWC.RemoteWebView
{
    public class GrpcBaseUriResponse
    {
        [JsonPropertyName("grpcBaseUri")]
        public String GrpcBaseUri {  get; set; } = string.Empty;
    }
    public class StatusResponse
    {
        [JsonPropertyName("connected")]
        public bool Connected { get; set; }
    }
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
            services.AddResponseCompression(options => { options.MimeTypes.Concat(["application/octet-stream", "application/wasm"]); });
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
            app.UseCors("CorsPolicy");

            app.UseCookiePolicy();
#if AUTHORIZATION
            app.UseAuthentication();
            app.UseAuthorization();
#endif
            app.UseGrpcWeb();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles(new StaticFileOptions { FileProvider = app.ApplicationServices?.GetRequiredService<RemoteFileResolver>() });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<RemoteWebViewService>().AllowAnonymous();
                endpoints.MapGrpcService<ClientIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGet("/mirror/{id:guid}", Mirror()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/app/{id:guid}", Start()).ConditionallyRequireAuthorization();

                // Refresh from home page i.e. https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/
                endpoints.MapGet("/{id:guid}", StartOrRefresh()).ConditionallyRequireAuthorization();

                // Refresh from nested page i.e.https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
                endpoints.MapGet("/{id:guid}/{unused:alpha}", StartOrRefresh()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/status/{id:guid}", Status()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/grpcbaseuri", GrpcBaseUri()).ConditionallyRequireAuthorization();           
                endpoints.MapGet("/wait/{id:guid}", Wait()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/test", Version());
                endpoints.MapFallbackToFile("index.html");
            });
        }

        private RequestDelegate Mirror()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                if (ServiceDictionary.TryGetValue(guid, out var serviceState))
                {
                    if (serviceState.EnableMirrors && serviceState.InUse)
                    {
                        serviceState.User = context.User.GetDisplayName() ?? "";
                        serviceState.IsMirroredConnection.Add(context.Connection.Id);

                        if (serviceState.IPC.ClientResponseStream != null)
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" });
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}");

                        var home = serviceState.HtmlHostPath;
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fi = rfr.GetFileInfo($"/{guid}/{home}");
                        context.Response.ContentLength = fi.Length;
                        using Stream stream = fi.CreateReadStream();
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "text/html";
                        await stream.CopyToAsync(context.Response.Body);
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
                        serviceState.Cookies = context.Request.Cookies;
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

        private RequestDelegate Status()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;

                var response = new StatusResponse
                {
                    Connected = ServiceDictionary.ContainsKey(guid)
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonContext.Default.StatusResponse));
            };
        }

        private RequestDelegate GrpcBaseUri()
        {
            return async context =>
            {
                var baseUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/";

                var response = new GrpcBaseUriResponse
                {
                    GrpcBaseUri =  baseUri,
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonContext.Default.GrpcBaseUriResponse));
            };
        }

        private RequestDelegate Version()
        {
            return async context =>
            {
                // Get the version of the currently executing assembly
                var assembly = Assembly.GetExecutingAssembly();
                var assemblyVersion = assembly.GetName().Version?.ToString() ?? "Version not found";

                // Create the version string
                string versionString = $"Version {assemblyVersion}";

                // Set the response content type to plain text
                context.Response.ContentType = "text/plain";

                // Write the version string to the response
                await context.Response.WriteAsync(versionString);
            };
        }

        private RequestDelegate StartOrRefresh()
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
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fi = rfr.GetFileInfo($"/{guid}/{home}");
                        context.Response.ContentLength = fi.Length;
                        using Stream stream = fi.CreateReadStream();
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "text/html";
                        await stream.CopyToAsync(context.Response.Body);
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
                    }
                   
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
