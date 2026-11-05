using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Entry points for hosting the RemoteWebViewService, either as the standalone exe
    /// (<see cref="Run(string[])"/>) or inside another process such as a WPF app
    /// (<see cref="StartAsync(RemoteWebViewServerOptions, CancellationToken)"/>).
    /// </summary>
    public static class RemoteWebViewServer
    {
        /// <summary>Configuration key that turns off UseHttpsRedirection (set by the embedded host when it serves plain HTTP).</summary>
        internal const string HttpsRedirectionKey = "RemoteWebView:HttpsRedirection";
        /// <summary>Configuration key that tells Startup the host owns logging, so it must not clear providers or add the Event Log.</summary>
        internal const string EmbeddedKey = "RemoteWebView:Embedded";

        #region Standalone exe

        /// <summary>Runs the service the way RemoteWebViewService.exe always has. Blocks until shutdown.</summary>
        public static void Run(string[] args)
        {
            ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            CreateHostBuilder(args).Build().Run();
        }

        /// <summary>
        /// Blocking form kept for compatibility with the net9SplitNuget API: HTTPS on loopback with the
        /// ASP.NET Core development certificate. Prefer <see cref="StartAsync(RemoteWebViewServerOptions, CancellationToken)"/>.
        /// </summary>
        public static void Run(int port, int maxNumClients = int.MaxValue, IEnumerable<string>? frameAncestors = null)
        {
            var options = new RemoteWebViewServerOptions
            {
                Port = port,
                MaxClients = maxNumClients,
                FrameAncestors = frameAncestors?.ToList() ?? [],
                UseHttps = true,
            };
            using var server = StartAsync(options).GetAwaiter().GetResult();
            server.WaitForShutdownAsync().GetAwaiter().GetResult();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureKestrel(options =>
            {
                // Remote PC Diagnostics agents hold one AgentHub.Connect stream open indefinitely (heartbeat every 30 s).
                // Kestrel does not time out an HTTP/2 connection while a stream is active, and gRPC turns off the
                // minimum request body data rate for streaming calls. Server pings keep NAT/proxy mappings warm
                // in both directions and close connections whose peer vanished without a FIN.
                ConfigureKeepAlive(options);

                // Container mode (e.g. Railway TCP proxy): Kestrel terminates TLS itself on 0.0.0.0:<port>.
                var containerCert = LoadContainerCertificate();
                if (containerCert != null)
                {
                    int port = int.TryParse(Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_PORT"), out var p) ? p : 8443;
                    options.ListenAnyIP(port, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        listenOptions.UseHttps(containerCert);
                    });
                    return;
                }
                options.Listen(IPAddress.Loopback, 5001, listenOptions =>
                {
                    listenOptions.UseHttps();
                    //listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3; // http3 needs a real certificate
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                });
                string certPath = "C:\\Certificates\\DevCertificate_192.168.1.35.pfx";
                if (File.Exists(certPath))
                {
                    options.Listen(IPAddress.Parse("192.168.1.35"), 5002, listenOptions =>
                    {
                        listenOptions.UseHttps(certPath, "YourStrongPassword");
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });
                }
            });

            webBuilder.UseStartup<Startup>();
        });

        public static bool IsContainerMode =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_CERT_PEM")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_PFX_BASE64"));

        // PEM cert+key (preferred) or base64 PFX + REMOTEWEBVIEW_TLS_PFX_PASSWORD. Returns null when not configured.
        private static X509Certificate2? LoadContainerCertificate()
        {
            string? certPem = Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_CERT_PEM");
            string? keyPem = Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_KEY_PEM");
            if (!string.IsNullOrEmpty(certPem) && !string.IsNullOrEmpty(keyPem))
            {
                using var pem = X509Certificate2.CreateFromPem(certPem, keyPem);
                // Re-import so the private key is usable by SslStream on all platforms
                return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
            }
            string? pfxB64 = Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_PFX_BASE64");
            if (!string.IsNullOrEmpty(pfxB64))
                return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfxB64), Environment.GetEnvironmentVariable("REMOTEWEBVIEW_TLS_PFX_PASSWORD"));
            return null;
        }

        private static void ConfigureKeepAlive(KestrelServerOptions options)
        {
            options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
            options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(60);
        }

        #endregion

        #region Embedded

        /// <summary>Starts the service in this process on loopback, plain HTTP, on a free port.</summary>
        public static Task<EmbeddedRemoteWebViewServer> StartAsync(int port = 0, int maxNumClients = int.MaxValue, IEnumerable<string>? frameAncestors = null, CancellationToken cancellationToken = default) =>
            StartAsync(new RemoteWebViewServerOptions { Port = port, MaxClients = maxNumClients, FrameAncestors = frameAncestors?.ToList() ?? [] }, cancellationToken);

        /// <summary>
        /// Starts the service in this process and returns once Kestrel is listening. Does not block, does not
        /// change the current directory or thread pool settings, and does not read the host app's appsettings.json
        /// or environment variables. Dispose (or call StopAsync on) the result to shut it down.
        /// </summary>
        public static async Task<EmbeddedRemoteWebViewServer> StartAsync(RemoteWebViewServerOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();

            bool https = options.UseHttps || options.Certificate != null;
            ListenOptions? browserListen = null, grpcListen = null;
            var settings = new Dictionary<string, string?>
            {
                [EmbeddedKey] = "true",
                [HttpsRedirectionKey] = https ? "true" : "false",
            };
            foreach (var kv in options.Configuration)
                settings[kv.Key] = kv.Value;

            var host = new HostBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureHostConfiguration(c => c.AddInMemoryCollection(settings))
                .ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings))
                .ConfigureLogging(lb =>
                {
                    if (options.ConfigureLogging != null) options.ConfigureLogging(lb);
                })
                .ConfigureServices(services =>
                {
                    // The host app owns process lifetime: no Ctrl+C / SIGTERM / ProcessExit hooks.
                    services.Replace(ServiceDescriptor.Singleton<IHostLifetime, EmbeddedHostLifetime>());
                    services.AddSingleton(new MaxClientsOptions { MaxClients = options.MaxClients });
                    services.AddSingleton(new FrameAncestorsOptions(options.FrameAncestors));
                    services.AddTransient<IStartupFilter, EmbeddedStartupFilter>();
                })
                .ConfigureWebHost(web =>
                {
                    web.UseKestrel(k =>
                    {
                        ConfigureKeepAlive(k);
                        k.Listen(options.Address, options.Port, listen =>
                        {
                            browserListen = listen;
                            if (https)
                            {
                                // TLS: ALPN picks HTTP/1.1 (browsers) or HTTP/2 (gRPC client) on one port.
                                listen.Protocols = HttpProtocols.Http1AndHttp2;
                                if (options.Certificate != null) listen.UseHttps(options.Certificate);
                                else listen.UseHttps();
                            }
                            else
                            {
                                // Cleartext has no ALPN, and Kestrel answers HTTP/2 prior knowledge on an Http1AndHttp2
                                // endpoint with HTTP_1_1_REQUIRED. So browsers (HTTP/1.1, gRPC-Web) get this port and
                                // the native gRPC client gets a second, HTTP/2-only (h2c) port below.
                                listen.Protocols = HttpProtocols.Http1;
                            }
                        });
                        if (!https)
                            k.Listen(options.Address, options.GrpcPort, listen =>
                            {
                                grpcListen = listen;
                                listen.Protocols = HttpProtocols.Http2;
                            });
                    });
                    web.UseStartup<Startup>();
                })
                .Build();

            try
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                host.Dispose();
                throw;
            }

            // Kestrel writes the bound endpoint (real port when 0 was asked for) back into each ListenOptions.
            Uri baseUri = ToUri(https ? "https" : "http", browserListen!);
            Uri grpcUri = grpcListen != null ? ToUri("http", grpcListen) : baseUri;
            return new EmbeddedRemoteWebViewServer(host, baseUri, grpcUri);
        }

        private static Uri ToUri(string scheme, ListenOptions listen)
        {
            var ep = listen.IPEndPoint ?? throw new InvalidOperationException("Kestrel did not report a bound endpoint.");
            if (ep.Port == 0) throw new InvalidOperationException("Kestrel did not report the bound port.");
            return new UriBuilder(scheme, ep.Address.ToString(), ep.Port, "/").Uri;
        }

        #endregion
    }

    /// <summary>Settings for <see cref="RemoteWebViewServer.StartAsync(RemoteWebViewServerOptions, CancellationToken)"/>.</summary>
    public sealed class RemoteWebViewServerOptions
    {
        /// <summary>Address to listen on. Defaults to 127.0.0.1.</summary>
        public IPAddress Address { get; set; } = IPAddress.Loopback;

        /// <summary>Port for browsers. 0 (the default) picks a free port; read it back from <see cref="EmbeddedRemoteWebViewServer.Port"/>.</summary>
        public int Port { get; set; }

        /// <summary>
        /// Plain HTTP only: port for the native gRPC client (HTTP/2 without TLS). 0 (the default) picks a free port;
        /// read it back from <see cref="EmbeddedRemoteWebViewServer.GrpcUri"/>. Ignored with HTTPS, where one port serves both.
        /// </summary>
        public int GrpcPort { get; set; }

        /// <summary>
        /// Use HTTPS with the ASP.NET Core development certificate (or <see cref="Certificate"/>).
        /// Defaults to false: plain HTTP, which is only allowed on a loopback <see cref="Address"/>.
        /// </summary>
        public bool UseHttps { get; set; }

        /// <summary>Server certificate. Setting it implies <see cref="UseHttps"/>.</summary>
        public X509Certificate2? Certificate { get; set; }

        /// <summary>Maximum number of RemoteWebView clients (CreateWebView calls) at once. Extra clients get 503.</summary>
        public int MaxClients { get; set; } = int.MaxValue;

        /// <summary>
        /// Origins allowed to frame pages from this server ("'self'" or absolute origins such as
        /// "https://app.example.com"). Empty means no frame-ancestors policy and no Origin check.
        /// </summary>
        public IReadOnlyList<string> FrameAncestors { get; set; } = [];

        /// <summary>Logging setup for the embedded server's own logger factory. Default: no providers (silent).</summary>
        public Action<ILoggingBuilder>? ConfigureLogging { get; set; }

        /// <summary>Extra configuration values, e.g. "RemoteFilesOptions:UseServerCache".</summary>
        public IDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>();

        internal void Validate()
        {
            if (Port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
            if (GrpcPort is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(GrpcPort));
            if (MaxClients < 1) throw new ArgumentOutOfRangeException(nameof(MaxClients));
            if (!UseHttps && Certificate == null && !IPAddress.IsLoopback(Address))
                throw new InvalidOperationException($"Plain HTTP is only allowed on a loopback address; {Address} needs UseHttps or a Certificate.");
            foreach (var origin in FrameAncestors)
            {
                if (origin != "'self'" && !Uri.IsWellFormedUriString(origin, UriKind.Absolute))
                    throw new InvalidOperationException($"Invalid origin: {origin}");
            }
        }
    }

    /// <summary>A RemoteWebViewService running inside this process.</summary>
    public sealed class EmbeddedRemoteWebViewServer : IAsyncDisposable, IDisposable
    {
        private readonly IHost host;
        private int stopped;

        internal EmbeddedRemoteWebViewServer(IHost host, Uri baseUri, Uri grpcUri)
        {
            this.host = host;
            BaseUri = baseUri;
            GrpcUri = grpcUri;
        }

        /// <summary>
        /// Address for browsers, with the bound port, e.g. http://127.0.0.1:53817/. Use it as ServerUri on the
        /// RemoteBlazorWebView; the page for a client is BaseUri + "app/{id}".
        /// </summary>
        public Uri BaseUri { get; }

        /// <summary>
        /// Address for the native gRPC client. Use it as GrpcBaseUri on the RemoteBlazorWebView. Over plain HTTP this
        /// is a second, HTTP/2-only port; over HTTPS it equals <see cref="BaseUri"/>.
        /// </summary>
        public Uri GrpcUri { get; }

        /// <summary>The browser port actually bound (useful when 0 was requested).</summary>
        public int Port => BaseUri.Port;

        public IServiceProvider Services => host.Services;

        /// <summary>Completes when the server stops (StopAsync, Dispose, or the host's StopApplication).</summary>
        public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) => host.WaitForShutdownAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            await host.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).ConfigureAwait(false); }
            finally
            {
                if (host is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
                else host.Dispose();
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public sealed class FrameAncestorsOptions(IReadOnlyList<string> origins)
    {
        public IReadOnlyList<string> Origins { get; } = origins;
    }

    internal sealed class EmbeddedHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
