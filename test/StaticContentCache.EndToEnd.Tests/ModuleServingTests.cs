extern alias client;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClientETag = client::PeakSWC.RemoteWebView.ETagGenerator;

namespace StaticContentCache.EndToEnd.Tests;

// HTTP serving only. The Chrome hook tests stay on net11. Cache-Control assertions
// stay on net11 because this line has no desktop cache-provider plumbing.
public class ModuleServingTests(ServingHost server) : IClassFixture<ServingHost>
{

    [Fact]
    public void WebView_package_version_is_the_one_this_branch_resolves()
    {
#if NET9_0
        Assert.Equal("9.0.9", WebViewPackageFiles.Version());
#elif NET10_0
        Assert.Equal("10.0.13", WebViewPackageFiles.Version());
#else
        throw new InvalidOperationException("This serving port is net9 or net10.");
#endif
        Assert.NotEmpty(WebViewPackageFiles.BlazorWebViewScript());
        Assert.NotEmpty(WebViewPackageFiles.ModulesManifest());
    }

    [Fact]
    public async Task Baseline_initializer_bytes_match_the_file_served_on_the_remote_path()
    {
        var file = ServingFiles.ReadShared("Baseline.lib.module.js");
        var response = await server.GetAsync(server.BaselineId, "Baseline.lib.module.js");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Baseline_manifest_lists_the_initializer_and_is_served_byte_for_byte()
    {
        var file = ServingFiles.ReadShared("blazor.modules.json");
        var response = await server.GetAsync(server.BaselineId, "_framework/blazor.modules.json");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(served);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(["Baseline.lib.module.js"], json.RootElement.EnumerateArray().Select(item => item.GetString() ?? "").ToArray());

        var files = new MemoryFileProvider();
        files.Add("_framework/blazor.modules.json", file);
        Assert.Equal(ClientETag.GenerateETag(files.GetFileInfo("_framework/blazor.modules.json")), Header(response, "ETag"));
    }

    [Fact]
    public async Task Real_blazor_webview_script_is_served_byte_for_byte()
    {
        var file = WebViewPackageFiles.BlazorWebViewScript();
        var response = await server.GetAsync(server.BaselineId, "_framework/blazor.webview.js");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Package_fallback_manifest_parses_as_an_empty_array()
    {
        var package = WebViewPackageFiles.ModulesManifest();
        using var json = JsonDocument.Parse(package);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Package_fallback_manifest_is_served_byte_for_byte()
    {
        var file = WebViewPackageFiles.ModulesManifest();
        var response = await server.GetAsync(server.PackageId, "_framework/blazor.modules.json");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.NotEqual(Encoding.ASCII.GetBytes("[]"), served);
        using var json = JsonDocument.Parse(served);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Empty_manifest_is_served_byte_for_byte()
    {
        var file = ServingFiles.Read("empty-blazor.modules.json");
        var response = await server.GetAsync(server.EmptyId, "_framework/blazor.modules.json");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        using var json = JsonDocument.Parse(served);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Unmodified_initializer_returns_304_only_for_its_own_etag()
    {
        const string path = "Baseline.lib.module.js";
        var file = ServingFiles.ReadShared(path);
        var files = new MemoryFileProvider();
        files.Add(path, file);
        var first = await server.GetAsync(server.BaselineId, path);
        var etag = Header(first, "ETag");
        Assert.Equal(ClientETag.GenerateETag(files.GetFileInfo(path)), etag);

        var matched = await server.ConditionalGetAsync(server.BaselineId, path, etag!);
        Assert.Equal(HttpStatusCode.NotModified, matched.StatusCode);
        Assert.Empty(await matched.Content.ReadAsByteArrayAsync());

        var other = Convert.ToBase64String(SHA256.HashData("other-initializer"u8.ToArray()));
        var mismatched = await server.ConditionalGetAsync(server.BaselineId, path, other);
        Assert.Equal(HttpStatusCode.OK, mismatched.StatusCode);
        Assert.Equal(file, await mismatched.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Each_client_serves_its_own_manifest_and_initializer()
    {
        var baselineManifest = await Body(server.BaselineId, "_framework/blazor.modules.json");
        var otherManifest = await Body(server.OtherId, "_framework/blazor.modules.json");
        var baselineModule = await Body(server.BaselineId, "Baseline.lib.module.js");
        var otherModule = await server.GetAsync(server.OtherId, "Baseline.lib.module.js");
        var otherOwn = await Body(server.OtherId, "Other.lib.module.js");
        var baselineOther = await server.GetAsync(server.BaselineId, "Other.lib.module.js");

        Assert.Equal(ServingFiles.ReadShared("blazor.modules.json"), baselineManifest);
        Assert.NotEqual(baselineManifest, otherManifest);
        Assert.Equal(ServingFiles.ReadShared("Baseline.lib.module.js"), baselineModule);
        Assert.Equal(HttpStatusCode.NotFound, otherModule.StatusCode);
        Assert.Equal(ServingFiles.Read("Other.lib.module.js"), otherOwn);
        Assert.Equal(HttpStatusCode.NotFound, baselineOther.StatusCode);
    }

    [Fact]
    public async Task Nested_library_path_is_served_under_the_client_prefix()
    {
        var index = await server.GetAsync(server.NestedId, "index.html");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains($"<base href=\"/{server.NestedId}/\"", html, StringComparison.Ordinal);

        var file = ServingFiles.Read("Nested.lib.module.js");
        var response = await server.GetAsync(server.NestedId, "_content/Lib/Nested.lib.module.js");
        var served = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);

        var manifest = await Body(server.NestedId, "_framework/blazor.modules.json");
        using var json = JsonDocument.Parse(manifest);
        Assert.Equal("_content/Lib/Nested.lib.module.js", json.RootElement[0].GetString());
    }

    [Fact]
    public async Task Listed_modules_follow_the_rewritten_base_href()
    {
        var index = await server.GetAsync(server.AppId, "index.html");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains($"<base href=\"/{server.AppId}/\"", html, StringComparison.Ordinal);

        foreach (var (relative, marker) in new[]
        {
            ("_content/Lib/Lib.lib.module.js", "libBefore"),
            ("App.lib.module.js", "appBefore")
        })
        {
            var response = await server.GetAsync(server.AppId, relative);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(marker, body, StringComparison.Ordinal);
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Object_shaped_manifest_is_served_unchanged()
    {
        var file = ServingFiles.Read("object-blazor.modules.json");
        var served = await Body(server.ObjectId, "_framework/blazor.modules.json");
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
    }

    [Fact]
    public async Task Missing_file_matches_the_local_status_body_and_type()
    {
        const string relative = "no-such.lib.module.js";
        var response = await server.GetAsync(server.AppId, relative);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"There is no content at {relative}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Not_modified_requires_the_client_etag()
    {
        const string path = "_framework/blazor.modules.json";
        var first = await server.GetAsync(server.AppId, path);
        var expected = ClientETag.GenerateETag(server.AppFiles.GetFileInfo(path));
        Assert.Equal(expected, Header(first, "ETag"));

        var matched = await server.ConditionalGetAsync(server.AppId, path, expected);
        Assert.Equal(HttpStatusCode.NotModified, matched.StatusCode);
        Assert.Empty(await matched.Content.ReadAsByteArrayAsync());

        var synthetic = await server.ConditionalGetAsync(server.AppId, path, "etag");
        Assert.Equal(HttpStatusCode.OK, synthetic.StatusCode);
        Assert.Equal(server.AppFiles.GetFileInfo(path).Length, (await synthetic.Content.ReadAsByteArrayAsync()).LongLength);

        var other = await server.ConditionalGetAsync(server.AppId, path, Convert.ToBase64String(SHA256.HashData("other"u8.ToArray())));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    private async Task<byte[]> Body(Guid id, string relative)
    {
        var response = await server.GetAsync(id, relative);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
