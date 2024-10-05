
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView.EndPoints;
using PeakSWC.RemoteWebView.Pages;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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
            return fileInfo != null && fileInfo.Exists && !fileInfo.IsDirectory;
        }

        private ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> ServiceDictionary { get; } = new();
        private readonly ConcurrentDictionary<string, Channel<string>> serviceStateChannel = new();


        private readonly IConfiguration Configuration;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
#if AUTHORIZATION
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
                lb.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                // Configure EventLog provider
                lb.AddEventLog(eventLogSettings =>
                {
                    eventLogSettings.SourceName = "RemoteWebViewService"; 
                    eventLogSettings.LogName = "Application"; 
                    eventLogSettings.Filter = (category, level) =>
                    {
                        return level >= Microsoft.Extensions.Logging.LogLevel.Warning;
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
            // Bind RemoteFilesOptions from configuration
            services.Configure<RemoteFilesOptions>(Configuration.GetSection("RemoteFilesOptions"));

            // Register RemoteFilesOptions as a singleton for direct access if needed
            services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<RemoteFilesOptions>>().Value);

            // Add memory cache
            services.AddMemoryCache();

            // Register ServerFileSyncManager as a singleton
            services.AddSingleton<ServerFileSyncManager>();

            services.AddSingleton(ServiceDictionary);
            services.AddSingleton(serviceStateChannel);
            services.AddSingleton<ShutdownService>();

#if STATS
            services.AddSingleton<ServerStats>();
            services.AddSingleton<StatsInterceptor>();
#endif

            services.AddGrpc(options =>
            {
                options.EnableDetailedErrors = true;
                options.ResponseCompressionLevel = CompressionLevel.Optimal;
                options.ResponseCompressionAlgorithm = "gzip";
#if STATS
                options.Interceptors.Add<StatsInterceptor>();
#endif
            });
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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<RemoteWebViewService>().AllowAnonymous();
                endpoints.MapGrpcService<ClientIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");
                endpoints.MapGrpcService<BrowserIPCService>().EnableGrpcWeb().AllowAnonymous().RequireCors("CorsPolicy");

                endpoints.MapGet("/favicon.ico", Endpoints.Favicon()).AllowAnonymous();
                endpoints.MapGet("/mirror/{id:guid}", Endpoints.Mirror()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/app/{id:guid}", Endpoints.Start()).ConditionallyRequireAuthorization();

                // Refresh from home page i.e. https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/
                endpoints.MapGet("/{id:guid}", Endpoints.StartOrRefresh()).ConditionallyRequireAuthorization();

                // Refresh from nested page i.e.https://localhost/9bfd9d43-0289-4a80-92d8-6e617729da12/counter
                endpoints.MapGet("/{id:guid}/{unused:alpha}", Endpoints.StartOrRefresh()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/status/{id:guid}", Endpoints.Status()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/grpcbaseuri", Endpoints.GrpcBaseUri()).ConditionallyRequireAuthorization();           
                endpoints.MapGet("/wait/{id:guid}", Endpoints.Wait()).ConditionallyRequireAuthorization();
#if STATS
                endpoints.MapGet("/stats", Endpoints.Stats()).ConditionallyRequireAuthorization();
                endpoints.MapGet("/stats/reset", Endpoints.ResetStats()).ConditionallyRequireAuthorization();
#endif
                endpoints.MapGet("/health", Endpoints.Health());
                endpoints.MapGet("/", Endpoints.Contact());
                endpoints.MapFallbackToFile("index.html");
            });
            app.UseRemoteFiles();
        }

   
    }
}
