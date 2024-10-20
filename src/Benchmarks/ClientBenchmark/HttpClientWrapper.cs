﻿
namespace ClientBenchmark
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;

    public class HttpClientWrapper
    {
        private const int MaxRetries = 1;
        private const int RetryDelayMilliseconds = 1000;
        private object lockObject = new object();
        private readonly HttpClient httpClient;

        public HttpClientWrapper()
        {
            // Configure HttpClient to use HTTP/3 with HTTP/2 fallback
            var handler = new HttpClientHandler();
            handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls13 | System.Security.Authentication.SslProtocols.Tls12; // Support TLS 1.2 for HTTP/2 fallback

            httpClient = new HttpClient(handler);
        }

        public async Task<string> GetWithRetryAsync(string url)
        {
            int attempts = 0;
            Version httpVersion = new Version(3, 0); // Start with HTTP/3

            while (attempts < MaxRetries)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Version = httpVersion;

                    var response = await httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        var data = await response.Content.ReadAsStringAsync();
                        lock (lockObject)
                        {
                            bytes += data.Length;
                            count++;
                        }
                        return data;
                    }
                    else if (httpVersion.Major == 3 && response.StatusCode == HttpStatusCode.UpgradeRequired)
                    {
                        // HTTP/3 not supported, fallback to HTTP/2
                        Console.WriteLine("HTTP/3 not supported, falling back to HTTP/2");
                        httpVersion = new Version(2, 0);
                    }
                    else
                    {
                        Console.WriteLine($"Attempt {attempts + 1} failed. Status code: {response.StatusCode}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Attempt {attempts + 1} failed. Error: {ex.Message}");
                }

                attempts++;
                if (attempts < MaxRetries)
                {
                    await Task.Delay(RetryDelayMilliseconds);
                }
            }

            throw new Exception($"Failed to get successful response after {MaxRetries} attempts");
        }

        // Assuming these are class-level variables
        public int bytes;
        public int count;
    }
}
