using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;

namespace StaticContentCache.Tests;

public class StaticContentCacheControlTests
{
    [Fact]
    public void Provider_null_leaves_metadata_unset()
    {
        var metadata = new FileMetadata();

        ClientFileSyncManager.ApplyStaticContentCacheControl(metadata, null, "/images/catalog/a.png", "image/png", NullLogger.Instance);

        Assert.Equal(string.Empty, metadata.CacheControl);
    }

    [Fact]
    public void Provider_result_is_stored_with_app_relative_uri_and_content_type()
    {
        BlazorWebViewStaticContentRequest? seen = null;
        var metadata = new FileMetadata();

        ClientFileSyncManager.ApplyStaticContentCacheControl(
            metadata,
            request =>
            {
                seen = request;
                return request.Uri.AbsolutePath.StartsWith("/images/catalog/", StringComparison.Ordinal)
                    && request.ContentType.StartsWith("image/", StringComparison.Ordinal)
                    ? "public, max-age=86400"
                    : null;
            },
            "/images/catalog/a.png?v=2",
            "image/png",
            NullLogger.Instance);

        Assert.Equal("public, max-age=86400", metadata.CacheControl);
        Assert.NotNull(seen);
        Assert.Equal("/images/catalog/a.png", seen!.Uri.AbsolutePath);
        Assert.Equal("?v=2", seen.Uri.Query);
        Assert.Equal("https://0.0.0.1/images/catalog/a.png?v=2", seen.Uri.AbsoluteUri);
        Assert.Equal("image/png", seen.ContentType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("public\r\nX-Injected: 1")]
    public void Null_empty_whitespace_and_newlines_become_the_maui_default(string? returned)
    {
        var metadata = new FileMetadata();

        ClientFileSyncManager.ApplyStaticContentCacheControl(
            metadata,
            _ => returned,
            "/app.css",
            "text/css",
            NullLogger.Instance);

        Assert.Equal(StaticContentCacheControl.Default, metadata.CacheControl);
    }

    [Fact]
    public void Provider_exception_becomes_the_maui_default()
    {
        var metadata = new FileMetadata();

        ClientFileSyncManager.ApplyStaticContentCacheControl(
            metadata,
            _ => throw new InvalidOperationException("boom"),
            "/app.css",
            "text/css",
            NullLogger.Instance);

        Assert.Equal(StaticContentCacheControl.Default, metadata.CacheControl);
    }

    [Fact]
    public void Oversized_value_becomes_the_maui_default()
    {
        var metadata = new FileMetadata();
        var oversized = new string('a', StaticContentCacheControl.MaxLength + 1);

        ClientFileSyncManager.ApplyStaticContentCacheControl(metadata, _ => oversized, "/app.css", "text/css", NullLogger.Instance);

        Assert.Equal(StaticContentCacheControl.Default, metadata.CacheControl);
        metadata.CacheControl = "";
        ClientFileSyncManager.ApplyStaticContentCacheControl(metadata, _ => new string('b', StaticContentCacheControl.MaxLength), "/app.css", "text/css", NullLogger.Instance);
        Assert.Equal(new string('b', StaticContentCacheControl.MaxLength), metadata.CacheControl);
    }

    [Theory]
    [InlineData("/images/my file.png?name=a%20b&v=2", "/images/my%20file.png", "?name=a%20b&v=2")]
    [InlineData("/images/my%20file.png?v=2", "/images/my%20file.png", "?v=2")]
    [InlineData("css/app.css?x=1", "/css/app.css", "?x=1")]
    public void Query_string_and_encoded_paths_reach_the_provider(string requestUri, string absolutePath, string query)
    {
        BlazorWebViewStaticContentRequest? seen = null;
        var metadata = new FileMetadata();

        ClientFileSyncManager.ApplyStaticContentCacheControl(
            metadata,
            request =>
            {
                seen = request;
                return "public, max-age=60";
            },
            requestUri,
            "text/css",
            NullLogger.Instance);

        Assert.NotNull(seen);
        Assert.Equal(absolutePath, seen!.Uri.AbsolutePath);
        Assert.Equal(query, seen.Uri.Query);
        Assert.Equal("https://0.0.0.1" + absolutePath + query, seen.Uri.AbsoluteUri);
        Assert.Equal("public, max-age=60", metadata.CacheControl);
    }
}
