using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
            {
                // Note: If appsettings.json does not exist in Azure the following configuration will fail with a certificate error
                // if (!File.Exists("appsettings.json"))
                   webBuilder.ConfigureKestrel(options => 
                   { 
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
                               //listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3; // http3 needs a real certificate
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
    }
}
