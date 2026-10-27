using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;

namespace StaticContentCache.Tests;

public class MissingFileResponseTests
{
    [Fact]
    public async Task Missing_file_matches_the_local_body_type_and_cache_header()
    {
        var clientId = Guid.NewGuid();
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
        state.FileManagerReady.SetResult(true);
        var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult(state);
        var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>
        {
            [clientId.ToString()] = ready
        };

        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddMemoryCache();
                    services.AddSingleton(new RemoteFilesOptions { UseClientCache = false, UseServerCache = false });
                    services.AddSingleton(new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance));
                    services.AddSingleton<RemoteFileResolver>(new MissingFileResolver());
                    services.AddSingleton(states);
                });
                web.Configure(app => app.UseMiddleware<RemoteFilesMiddleware>());
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync($"/{clientId}/no-such.js");
        var cache = response.Headers.TryGetValues("Cache-Control", out var values) ? string.Join(",", values) : "";

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("There is no content at no-such.js", await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["max-age=0", "must-revalidate", "no-cache", "no-store"],
            cache.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(directive => directive, StringComparer.Ordinal)
                .ToArray());
    }

    private sealed class MissingFileResolver : RemoteFileResolver
    {
        public MissingFileResolver()
            : base(NullLogger<RemoteFileResolver>.Instance, new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance))
        {
        }

        public override Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath)
            => Task.FromResult(new FileMetadata
            {
                Length = -1,
                StatusCode = StatusCodes.Status404NotFound,
                ETag = "",
                LastModified = 0
            });
    }
}
