using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using ServiceFileStream = PeakSWC.RemoteWebView.RemoteStaticFiles.FileStream;

namespace StaticContentCache.Tests;

public class MismatchedContentLengthTests
{
    [Fact]
    public async Task Response_body_is_not_cut_to_the_metadata_length()
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
        var resolver = new LongerThanMetadataResolver();

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
                    services.AddSingleton<RemoteFileResolver>(resolver);
                    services.AddSingleton(states);
                });
                web.Configure(app => app.UseMiddleware<RemoteFilesMiddleware>());
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync($"/{clientId}/wwwroot/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        Assert.Equal(5, response.Content.Headers.ContentLength);
    }

    private sealed class LongerThanMetadataResolver : RemoteFileResolver
    {
        public LongerThanMetadataResolver()
            : base(NullLogger<RemoteFileResolver>.Instance, new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance))
        {
        }

        public override Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath, string? requestUri = null, string? contentType = null)
            => Task.FromResult(new FileMetadata
            {
                Length = 3,
                StatusCode = StatusCodes.Status200OK,
                ETag = "etag",
                LastModified = 0
            });

        public override Task<ServiceFileStream> GetFileStreamAsync(string clientId, string subpath)
            => Task.FromResult(new ServiceFileStream { Stream = new MemoryStream(Encoding.UTF8.GetBytes("hello")) });
    }
}
