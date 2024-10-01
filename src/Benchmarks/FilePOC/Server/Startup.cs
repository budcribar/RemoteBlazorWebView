// Startup.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using Grpc.Net.Client.Balancer;

namespace PeakSWC.RemoteWebView
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // Configure services and bind RemoteFilesOptions
        public void ConfigureServices(IServiceCollection services)
        {
            // Bind RemoteFilesOptions from configuration
            services.Configure<RemoteFilesOptions>(Configuration.GetSection("RemoteFilesOptions"));
          
            // Register RemoteFilesOptions as a singleton for direct access if needed
            services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<RemoteFilesOptions>>().Value);

            // Add gRPC services
            services.AddGrpc();

            // Add memory cache
            services.AddMemoryCache();

            // Register ServerFileSyncManager as a singleton
            services.AddSingleton<ServerFileSyncManager>();

            services.AddSingleton<ConcurrentDictionary<string, ServiceState>>();

            services.AddTransient<RemoteFileResolver>();
        }

        // Configure the HTTP request pipeline
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, RemoteFilesOptions remoteFilesOptions)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Ensure the RootDirectory exists
            System.IO.Directory.CreateDirectory(remoteFilesOptions.RootDirectory);

            app.UseRouting();

            // Add the Health Check Endpoint
            app.UseEndpoints(endpoints =>
            {
                endpoints.Map("/health", async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsync("OK");
                });

                endpoints.Map("/favicon.ico", async context =>
                {
                    byte[] faviconBytes = CreateSimpleFavicon();

                    context.Response.ContentType = "image/x-icon";
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.Body.WriteAsync(faviconBytes, 0, faviconBytes.Length);
                });

                endpoints.MapGet("/cache/{cacheType}/{action}", async (HttpContext context, RemoteFilesOptions options, string cacheType, string action) =>
                {
                    // Normalize input to lowercase to make the API case-insensitive
                    cacheType = cacheType.ToLower();
                    action = action.ToLower();

                    // Parse the action to a boolean
                    bool result = action == "enable";

                    // Determine the cache type and set the corresponding property
                    switch (cacheType)
                    {
                        case "server":
                            options.UseServerCache = result;
                            await context.Response.WriteAsync($"Server cache {(result ? "enabled" : "disabled")}.");
                            break;

                        case "client":
                            options.UseClientCache = result;
                            await context.Response.WriteAsync($"Client cache {(result ? "enabled" : "disabled")}.");
                            break;

                        default:
                            // Invalid cache type
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Invalid cache type. Use 'server' or 'client'.");
                            return;
                    }

                    // Map gRPC services
                    endpoints.MapGrpcService<FileSyncServiceImpl>();

                    // Default route for non-gRPC requests
                    endpoints.MapGet("/", async context =>
                    {
                        await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client.");
                    });

                });

                // Get Current Cache Status
                endpoints.MapGet("/cache", async (HttpContext context, RemoteFilesOptions options) =>
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    string response = $"UseServerCache: {options.UseServerCache}, UseClientCache: {options.UseClientCache}";
                    await context.Response.WriteAsync(response);
                });
                // Map gRPC services
                endpoints.MapGrpcService<FileSyncServiceImpl>();
            });

            // Add the UseRemoteFiles middleware after routing and before endpoints
            app.UseRemoteFiles();
        }
        private static byte[] CreateSimpleFavicon()
        {
            // This creates a 16x16 transparent ICO file
            byte[] faviconBytes = new byte[]
            {
        0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10, 0x00, 0x00, 0x01, 0x00,
        0x20, 0x00, 0x68, 0x04, 0x00, 0x00, 0x16, 0x00, 0x00, 0x00, 0x28, 0x00,
        0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };

            return faviconBytes;
        }
    }
}
