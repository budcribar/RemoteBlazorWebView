namespace ClientBenchmark
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    public class HttpClientWrapper
    {
        private const int MaxRetries = 1;
        private const int RetryDelayMilliseconds = 1000;
        private readonly HttpClient httpClient;

        public int bytes;
        public int count;

        public HttpClientWrapper(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task<string> GetWithRetryAsync(string url)
        {
            int attempts = 0;
            while (attempts < MaxRetries)
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var data = await response.Content.ReadAsStringAsync();
                        Interlocked.Add(ref bytes, data.Length);
                        Interlocked.Increment(ref count);
                        return data;
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
    }
}
