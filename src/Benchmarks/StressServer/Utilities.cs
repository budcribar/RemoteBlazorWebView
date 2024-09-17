﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
namespace ClientBenchmark
{
    

    public class CertificateInstaller
    {
        public static X509Certificate2 LoadCerCertificate(string cerFilePath)
        {
            return X509CertificateLoader.LoadCertificateFromFile(cerFilePath);
        }
        public static void AddCertificateToLocalMachine(string cerFilePath)
        {
            try
            {
                // Load the certificate
                X509Certificate2 certificate = LoadCerCertificate(cerFilePath);

                // Open the Local Machine's Trusted Root store
                using (X509Store store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite);

                    // Check if the certificate already exists
                    bool exists = false;
                    foreach (var cert in store.Certificates)
                    {
                        if (cert.Thumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        store.Add(certificate);
                        Console.WriteLine("Certificate added to Local Machine's Trusted Root store.");
                    }
                    else
                    {
                        Console.WriteLine("Certificate already exists in Local Machine's Trusted Root store.");
                    }

                    store.Close();
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Error: Access denied. Please run the application with the necessary permissions.");
            }
            catch (CryptographicException ex)
            {
                Console.WriteLine($"Cryptographic error: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }
        }


    }

    public static class Utilities
    {
        private static readonly char[] chars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();

        public static string GenerateRandomString(int length)
        {
            StringBuilder result = new StringBuilder(length);
            Random random = new Random();

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }

            return result.ToString();
        }
        public static void KillExistingProcesses(string processName)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    Console.WriteLine($"Killing process: {process.ProcessName} (ID: {process.Id})");
                    process.Kill();
                    process.WaitForExit(); // Optionally wait for the process to exit
                }
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Error killing process: {ex.Message}");
            }
        }

        public static async Task PollHttpRequest(HttpClient httpClient, string url)
        {
            bool serverStarted = false;
            while (!serverStarted)
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    serverStarted = response.IsSuccessStatusCode;
                }
                catch
                {
                    // Server is not ready yet, wait a bit before retrying
                    await Task.Delay(10);
                }
            }
        }

        public static void TryVariousPorts()
        {
            AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);

            using var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true // Only for testing!
                }
            };
            // using var client = new HttpClient();
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            string[] urls = new[]
            {
            "https://localhost:5001",
            "https://localhost:5002",
            "https://localhost:5003", // New HTTP/3-only endpoint
            "https://127.0.0.1:5001",
            "https://127.0.0.1:5002",
            "https://127.0.0.1:5003"  // New HTTP/3-only endpoint
        };

            foreach (var url in urls)
            {
                try
                {
                    Console.WriteLine($"Attempting to connect to {url}");
                    var response = client.GetAsync(url).Result;
                    Console.WriteLine($"Connected to {url}");
                    Console.WriteLine($"Status: {response.StatusCode}");
                    Console.WriteLine($"Protocol: {response.Version}");
                  

                    if (response.Headers.TryGetValues("alt-svc", out var altSvcValues))
                    {
                        Console.WriteLine($"Alt-Svc header: {string.Join(", ", altSvcValues)}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Error connecting to {url}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error connecting to {url}: {ex.Message}");
                }
                Console.WriteLine();
            }

        }
    }
}