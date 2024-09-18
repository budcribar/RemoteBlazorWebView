
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView.Pages;
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
using System.Threading;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using IFileProvider = PeakSwc.StaticFiles.IFileProvider;
#if AUTHORIZATION
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using System.Net.Http;
using Microsoft.Identity.Web.UI;
#endif

namespace PeakSWC.RemoteWebView
{
    public class RateLimitStats
    {
        private long _totalCount;
        private long _successCount;
        private long _rejectedCount;

        public void IncrementTotalCount() => Interlocked.Increment(ref _totalCount);
        public void IncrementSuccessCount() => Interlocked.Increment(ref _successCount);
        public void IncrementRejectedCount() => Interlocked.Increment(ref _rejectedCount);

        public (long Total, long Success, long Rejected) GetStats() =>
            (_totalCount, _successCount, _rejectedCount);
    }

    public class RateLimitMonitoringService : BackgroundService
    {
        private readonly ILogger<RateLimitMonitoringService> _logger;
        private readonly RateLimitStats _stats;

        public RateLimitMonitoringService(ILogger<RateLimitMonitoringService> logger, RateLimitStats stats)
        {
            _logger = logger;
            _stats = stats;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var (total, success, rejected) = _stats.GetStats();
                //_logger.LogInformation("Rate Limit Stats - Total: {Total}, Success: {Success}, Rejected: {Rejected}", total, success, rejected);
                _logger.LogWarning("Rate Limit Stats - Total: {Total}, Success: {Success}, Rejected: {Rejected}", total, success, rejected);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
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
        private async Task<bool> IsStaticFileRequest(HttpContext context, IFileProvider fileProvider)
        {
            var filePath = context.Request.Path.Value?.TrimStart('/');
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            var fileInfo = await fileProvider.GetFileInfo(filePath).ConfigureAwait(false);
            return fileInfo.Exists && !fileInfo.IsDirectory;
        }

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
            AuthenticationResult result = await confidentialClientApplication.AcquireTokenForClient(scopes).ExecuteAsync().ConfigureAwait(false);
            var httpClient = new HttpClient();
            return new ProtectedApiCallHelper(httpClient,result.AccessToken);
        }
#endif

        public void ConfigureServices(IServiceCollection services)
        {
#if !DEBUG
            services.AddLogging(lb =>
            {

                lb.ClearProviders();
                // Configure EventLog provider
                lb.AddEventLog(eventLogSettings =>
                {
                    //eventLogSettings.SourceName = "RemoteWebViewService"; 
                    eventLogSettings.LogName = "Application"; 
                    eventLogSettings.Filter = (category, level) =>
                    {
                        return level >= Microsoft.Extensions.Logging.LogLevel.Information;
                        //return level >= Microsoft.Extensions.Logging.LogLevel.Warning;
                    };
                });

            });
#endif
            services.AddResponseCompression(options => { options.MimeTypes.Concat(["application/octet-stream", "application/wasm"]); });
#if RATELIMIT
            services.AddSingleton<RateLimitStats>();
            services.AddHostedService<RateLimitMonitoringService>();
            services.AddRateLimiter(options =>
            {
                options.AddPolicy("StaticFilesRateLimit", context =>
                {
                    if (IsStaticFileRequest(context, context.RequestServices.GetRequiredService<RemoteFileResolver>()))
                    {
                        return RateLimitPartition.GetConcurrencyLimiter(
                            partitionKey: "static_file_limiter",
                            factory: _ => new ConcurrencyLimiterOptions
                            {
                                PermitLimit = 100,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                QueueLimit = 1000
                            });
                    }
                    return RateLimitPartition.GetNoLimiter("no_limit");
                });

                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", token).ConfigureAwait(false);

                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
                    logger.LogWarning("Rate limit exceeded for static file: {RequestPath}", context.HttpContext.Request.Path);

                    var stats = context.HttpContext.RequestServices.GetRequiredService<RateLimitStats>();
                    stats.IncrementRejectedCount();
                };

                options.RejectionStatusCode = 429;
            });
#endif
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
#if RATELIMIT
           

            app.Use(async (context, next) =>
            {
                var fileProvider = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                var isStaticFile = IsStaticFileRequest(context, fileProvider);

                if (isStaticFile)
                {
                    var stats = context.RequestServices.GetRequiredService<RateLimitStats>();
                    stats.IncrementTotalCount();
                }

                await next().ConfigureAwait(false);

                if (isStaticFile && context.Response.StatusCode != 429)
                {
                    var stats = context.RequestServices.GetRequiredService<RateLimitStats>();
                    stats.IncrementSuccessCount();
                }
            });

            app.UseRateLimiter();
#endif
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
            app.UseStaticFiles(new PeakSWC.RemoteWebView.StaticFileOptions { FileProvider = app.ApplicationServices?.GetRequiredService<RemoteFileResolver>() });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<RemoteWebViewService>().AllowAnonymous();
                endpoints.MapGrpcService<ClientIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGet("/favicon.ico", Favicon()).AllowAnonymous();
                endpoints.MapGet("/mirror/{id:guid}", Mirror()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/app/{id:guid}", Start()).ConditionallyRequireAuthorization();

                // Refresh from home page i.e. https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/
                endpoints.MapGet("/{id:guid}", StartOrRefresh()).ConditionallyRequireAuthorization();

                // Refresh from nested page i.e.https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
                endpoints.MapGet("/{id:guid}/{unused:alpha}", StartOrRefresh()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/status/{id:guid}", Status()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/grpcbaseuri", GrpcBaseUri()).ConditionallyRequireAuthorization();           
                endpoints.MapGet("/wait/{id:guid}", Wait()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/", Contact());
                endpoints.MapFallbackToFile("index.html");
            });
        }

        private RequestDelegate Favicon()
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
                    return;
                }

                await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
            };
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
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" }).ConfigureAwait(false);
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}").ConfigureAwait(false);

                        var home = serviceState.HtmlHostPath;
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fi = await rfr.GetFileInfo($"/{guid}/{home}").ConfigureAwait(false);
                        context.Response.ContentLength = fi.Length;
                        using Stream stream = fi.CreateReadStream();
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "text/html";
                        await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync("Mirroring is not enabled").ConfigureAwait(false);
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
                        await context.Response.WriteAsync(LockedPage.Html(serviceState.User, guid)).ConfigureAwait(false);
                    }
                    else
                    {
                        serviceState.Cookies = context.Request.Cookies;
                        serviceState.InUse = true;
                        serviceState.User = context.User.GetDisplayName() ?? "";
                       
                        if (serviceState.IPC.ClientResponseStream != null)
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" }).ConfigureAwait(false);
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}").ConfigureAwait(false);
                       
                        context.Response.Redirect($"/{guid}");
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid, false)).ConfigureAwait(false);
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
                        await context.Response.WriteAsync($"Wait completed").ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }

                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(RestartFailedPage.Fragment(guid)).ConfigureAwait(false);
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
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonContext.Default.StatusResponse)).ConfigureAwait(false);
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
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonContext.Default.GrpcBaseUriResponse)).ConfigureAwait(false);
            };
        }

        private RequestDelegate Contact()
        {
            return async context =>
            {
                // Get the version of the currently executing assembly
                var assembly = Assembly.GetExecutingAssembly();
                var assemblyVersion = assembly.GetName().Version?.ToString() ?? "Version not found";

                // Create the version string
                string versionString = $"Version {assemblyVersion}";

                var contact = new ContactInfo { Company = "Peak Software Consulting, LLC", Email = "budcribar@msn.com", Name= "Bud Cribar", Url = "https://github.com/budcribar/RemoteBlazorWebView" };
                var html = HtmlPageGenerator.GenerateContactPage(contact, versionString);

                context.Response.ContentType = "text/html";

                // Write the version string to the response
                await context.Response.WriteAsync(html).ConfigureAwait(false);
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
                        var fi = await rfr.GetFileInfo($"/{guid}/{home}").ConfigureAwait(false);
                        context.Response.ContentLength = fi.Length;
                        using Stream stream = fi.CreateReadStream();
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "text/html";
                        await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                    }
                    else
                    {
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = "refreshed:" }).ConfigureAwait(false);

                        // Wait until client shuts down 
                        for (int i = 0; i < 3000; i++)
                        {
                            if (!ServiceDictionary.ContainsKey(guid))
                            {
                                context.Response.ContentType = "text/html";
                                await context.Response.WriteAsync(RestartPage.Html(guid, serviceState?.ProcessName ?? "", serviceState?.HostName ?? "")).ConfigureAwait(false);
                                return;
                            }

                            await Task.Delay(10).ConfigureAwait(false);
                        }

                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";

                        await context.Response.WriteAsync(RestartFailedPage.Html(serviceState.ProcessName, serviceState.Pid, serviceState.HostName)).ConfigureAwait(false);

                        // Shutdown since client did not respond to restart request
                        await context.RequestServices.GetRequiredService<ShutdownService>().Shutdown(guid);
                    }
                   
                }
                else
                {
                    context.Response.StatusCode = 400;                  
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid,true)).ConfigureAwait(false);
                }
            };
        }
    }
}
