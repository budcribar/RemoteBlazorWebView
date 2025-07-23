using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text; // Add this for WriteAsync extension
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public static class RemoteWebViewServer
    {
        public static void Run(string[] args)
        {
            ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
            CreateHostBuilder(args).Build().Run();
        }

        public static void Run(int port, int maxNumClients = int.MaxValue, IEnumerable<string>? frameAncestors = null)
        {
            foreach (var origin in frameAncestors ?? [])
            {
                if (origin != "'self'" && !Uri.IsWellFormedUriString(origin, UriKind.Absolute))
                {
                    throw new InvalidOperationException($"Invalid origin: {origin}");
                }
            }

            ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
            CreateHostBuilderWithLimits(port, maxNumClients, frameAncestors).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 5002, listenOptions =>
                    {
                        listenOptions.UseHttps();
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });
                    string certPath = "C:\\Certificates\\DevCertificate_192.168.1.35.pfx";
                    if (File.Exists(certPath))
                        options.Listen(IPAddress.Parse("192.168.1.35"), 5002, listenOptions =>
                        {
                            listenOptions.UseHttps(certPath, "YourStrongPassword");
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        });
                });

                webBuilder.UseStartup<Startup>();
            });

        public static IHostBuilder CreateHostBuilderWithLimits(int port, int maxNumClients, IEnumerable<string>? frameAncestors) => Host.CreateDefaultBuilder().ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, port, listenOptions =>
                {
                    listenOptions.UseHttps();
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                });
            });
            webBuilder.UseStartup(ctx => new StartupWithClientLimit(maxNumClients, frameAncestors));
        });
    }

    // Custom Startup to enforce maxNumClients
    public class StartupWithClientLimit : Startup
    {
        private readonly int _maxNumClients;
        private readonly IEnumerable<string> _frameAncestors;
        public StartupWithClientLimit(int maxNumClients, IEnumerable<string>? frameAncestors) : base(new ConfigurationBuilder().Build())
        {
            _maxNumClients = maxNumClients;
            _frameAncestors = frameAncestors ?? Array.Empty<string>();
        }

        public new void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddSingleton(new MaxClientsOptions { MaxClients = _maxNumClients });
        }

        public new void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Middleware to enforce maxNumClients
            var maxClientsOptions = app.ApplicationServices.GetService<MaxClientsOptions>();
            var serviceDictionary = app.ApplicationServices.GetService(typeof(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>)) as ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>;
            app.Use(async (context, next) =>
            {
                if (serviceDictionary != null && maxClientsOptions != null && serviceDictionary.Count >= maxClientsOptions.MaxClients)
                {
                    context.Response.StatusCode = 503;
                    var message = "Server is at maximum client capacity.";
                    var buffer = Encoding.UTF8.GetBytes(message);
                    context.Response.ContentType = "text/plain";
                    await context.Response.Body.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }
                await next();
            });

            if(_frameAncestors?.Count() > 0)    
            app.Use(async (context, next) =>
            {
                // Check Origin header for iframe embedding
                var origin = context.Request.Headers["Origin"].ToString();
                if (!string.IsNullOrEmpty(origin) && !_frameAncestors.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Forbidden: Origin not allowed");
                    return;
                }

                await next();

                // Only attach CSP header to HTML responses
                if (_frameAncestors.Any() &&
                    context.Response.ContentType != null &&
                    context.Response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var policy = $"frame-ancestors {string.Join(' ', _frameAncestors)}";
                    context.Response.Headers["Content-Security-Policy"] = policy;
                }
            });


            base.Configure(app, env);
        }
    }
}
