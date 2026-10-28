extern alias client;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.StaticFiles;
using ClientETag = client::PeakSWC.RemoteWebView.ETagGenerator;

namespace StaticContentCache.EndToEnd.Tests;

// The initializer bytes are the on-disk Baseline.lib.module.js asset. SHA-256 and
// lengths are computed from that file (and from blazor.webview.js in the WebView
// package cache) when the test runs.
//
// Hook checks load the real Microsoft.AspNetCore.Components.WebView blazor.webview.js
// through RemoteFilesMiddleware. They are not a reimplementation of the loader.
// module-initializer-probe.js is the only test hook: it stubs window.external and
// records boot failures. The initializer itself writes the flags the assertions read.
//
// Local WebView 11 boot (blazor.webview.js): fetch _framework/blazor.modules.json,
// map each JSON string to {name}, import those URLs against document.baseURI in
// Promise.all, call beforeStart with no arguments, then after sendAttachPage call
// afterStarted(window.Blazor). A rejection in that Promise.all rejects boot before
// AttachPage and before afterStarted. These tests record that behavior.
public class ModuleInitializerParityTests(ModuleInitializerServer server) : IClassFixture<ModuleInitializerServer>
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [Fact]
    public async Task Baseline_initializer_bytes_match_the_file_served_on_the_remote_path()
    {
        var file = ModuleInitializerFiles.ReadShared("Baseline.lib.module.js");
        var response = await server.GetAsync(server.BaselineId, "Baseline.lib.module.js");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        Assert.Equal(LocalContentType("Baseline.lib.module.js"), response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Baseline_manifest_lists_the_initializer_and_is_served_byte_for_byte()
    {
        var file = ModuleInitializerFiles.ReadShared("blazor.modules.json");
        var response = await server.GetAsync(server.BaselineId, "_framework/blazor.modules.json");
        var served = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
        Assert.Equal(file.LongLength, response.Content.Headers.ContentLength);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(served);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        var names = json.RootElement.EnumerateArray().Select(item =>
        {
            Assert.Equal(JsonValueKind.String, item.ValueKind);
            return item.GetString() ?? "";
        }).ToArray();
        Assert.Equal(["Baseline.lib.module.js"], names);

        var etag = Header(response, "ETag");
        var files = new MutableFileProvider();
        files.Add("_framework/blazor.modules.json", file);
        var expected = ClientETag.GenerateETag(files.GetFileInfo("_framework/blazor.modules.json"));
        Assert.NotEqual("etag", etag);
        Assert.Equal(expected, etag);
    }

    [Fact]
    public async Task Real_blazor_webview_script_is_served_byte_for_byte()
    {
        var file = File.ReadAllBytes(ModuleInitializerFiles.WebViewScriptPath());
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
        var package = File.ReadAllBytes(ModuleInitializerFiles.PackageModulesManifestPath());
        using var json = JsonDocument.Parse(package);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Empty_manifest_is_served_byte_for_byte()
    {
        var file = ModuleInitializerFiles.Read("empty-blazor.modules.json");
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
        var file = ModuleInitializerFiles.ReadShared(path);
        var files = new MutableFileProvider();
        files.Add(path, file);
        var first = await server.GetAsync(server.BaselineId, path);
        var etag = Header(first, "ETag");
        Assert.Equal(ClientETag.GenerateETag(files.GetFileInfo(path)), etag);
        Assert.NotEqual("etag", etag);

        var matched = await server.ConditionalGetAsync(server.BaselineId, path, etag!);
        Assert.Equal(HttpStatusCode.NotModified, matched.StatusCode);
        Assert.Empty(await matched.Content.ReadAsByteArrayAsync());

        var other = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("other-initializer")));
        var mismatched = await server.ConditionalGetAsync(server.BaselineId, path, other);
        Assert.Equal(HttpStatusCode.OK, mismatched.StatusCode);
        Assert.Equal(file, await mismatched.Content.ReadAsByteArrayAsync());

        var synthetic = await server.ConditionalGetAsync(server.BaselineId, path, "etag");
        Assert.Equal(HttpStatusCode.OK, synthetic.StatusCode);
        Assert.Equal(file, await synthetic.Content.ReadAsByteArrayAsync());
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

        Assert.Equal(ModuleInitializerFiles.ReadShared("blazor.modules.json"), baselineManifest);
        Assert.NotEqual(baselineManifest, otherManifest);
        Assert.Equal(ModuleInitializerFiles.ReadShared("Baseline.lib.module.js"), baselineModule);
        Assert.Equal(HttpStatusCode.NotFound, otherModule.StatusCode);
        Assert.Equal(ModuleInitializerFiles.Read("Other.lib.module.js"), otherOwn);
        Assert.Equal(HttpStatusCode.NotFound, baselineOther.StatusCode);
    }

    [Fact]
    public async Task Nested_library_path_is_served_under_the_client_prefix()
    {
        var index = await server.GetAsync(server.NestedId, "index.html");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains($"<base href=\"/{server.NestedId}/\"", html);

        var file = ModuleInitializerFiles.Read("Nested.lib.module.js");
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
    public async Task Object_shaped_manifest_is_served_unchanged()
    {
        var file = ModuleInitializerFiles.Read("object-blazor.modules.json");
        var served = await Body(server.ObjectManifestId, "_framework/blazor.modules.json");
        Assert.Equal(file, served);
        Assert.Equal(Sha256(file), Sha256(served));
    }

    [HeadlessChromeFact]
    public async Task Baseline_hooks_run_once_and_beforeStart_precedes_afterStarted()
    {
        await using var page = await OpenAsync(server.BaselineId);
        var snap = await WaitForAsync(page, BaselineReady, "baseline hooks");
        var baseline = Object(snap, "baseline");
        Assert.Equal(1, Int(baseline, "beforeStart"));
        Assert.Equal(1, Int(baseline, "afterStarted"));
        Assert.Equal(["beforeStart", "afterStarted"], Strings(baseline["order"]));

        var moduleUrl = new Uri(baseline["url"]!.GetValue<string>());
        Assert.Equal($"/{server.BaselineId}/Baseline.lib.module.js", moduleUrl.AbsolutePath);
        Assert.Contains(page.RequestUrls, url => new Uri(url).AbsolutePath == moduleUrl.AbsolutePath);
        Assert.Contains(page.RequestUrls, url => new Uri(url).AbsolutePath == $"/{server.BaselineId}/_framework/blazor.modules.json");
        Assert.Contains(page.RequestUrls, url => new Uri(url).AbsolutePath == $"/{server.BaselineId}/_framework/blazor.webview.js");
        Assert.Equal($"/{server.BaselineId}/", new Uri(snap["baseHref"]!.GetValue<string>()).AbsolutePath);
        Assert.Contains(Ipc(snap), message => message.Contains("AttachPage", StringComparison.Ordinal));
        Assert.Null(BootError(snap));
    }

    [HeadlessChromeFact]
    public async Task A_second_start_on_the_same_page_does_not_run_hooks_again()
    {
        await using var page = await OpenAsync(server.BaselineId);
        await WaitForAsync(page, BaselineReady, "baseline hooks");
        var message = (await page.EvaluateAsync(
            "window.Blazor.start().then(() => 'resolved', (error) => error && error.message ? error.message : String(error))"))!
            .GetValue<string>();
        var snap = await page.SnapshotAsync();
        var baseline = Object(snap, "baseline");

        Assert.Equal("Blazor has already started.", message);
        Assert.Equal(1, Int(baseline, "beforeStart"));
        Assert.Equal(1, Int(baseline, "afterStarted"));
        Assert.Equal(["beforeStart", "afterStarted"], Strings(baseline["order"]));
    }

    [HeadlessChromeFact]
    public async Task Refresh_runs_hooks_once_on_the_new_page()
    {
        await using var page = await OpenAsync(server.BaselineId);
        await WaitForAsync(page, BaselineReady, "baseline hooks");
        await page.EvaluateAsync("window.__reloading = true");
        await page.ReloadAsync();
        var snap = await WaitForAsync(page, static s => !Flag(s, "reloading") && BaselineReady(s), "reloaded baseline hooks");
        var baseline = Object(snap, "baseline");

        Assert.Equal(1, Int(baseline, "beforeStart"));
        Assert.Equal(1, Int(baseline, "afterStarted"));
        Assert.Equal(["beforeStart", "afterStarted"], Strings(baseline["order"]));
    }

    [HeadlessChromeFact]
    public async Task Two_initializers_run_in_manifest_order()
    {
        await using var page = await OpenAsync(server.OrderId);
        var snap = await WaitForAsync(
            page,
            static s => Strings(s["order"]).Contains("first.afterStarted")
                && Strings(s["order"]).Contains("second.afterStarted"),
            "two initializers");
        var order = Strings(snap["order"]);
        Assert.Equal(4, order.Length);
        Assert.Equal(["first.beforeStart", "second.beforeStart"], order[..2].OrderBy(static x => x, StringComparer.Ordinal));
        Assert.Equal(["first.afterStarted", "second.afterStarted"], order[2..].OrderBy(static x => x, StringComparer.Ordinal));
        Assert.Equal(order[0].Split('.')[0], order[2].Split('.')[0]);
        Assert.Equal(order[1].Split('.')[0], order[3].Split('.')[0]);
    }

    [HeadlessChromeFact]
    public async Task Async_hooks_finish_before_the_next_stage()
    {
        await using var page = await OpenAsync(server.AsyncId);
        var snap = await WaitForAsync(page, static s => Strings(s["order"]).Contains("async.afterStarted:end"), "async hooks");
        var order = Strings(snap["order"]);
        var asyncStart = At(order, "async.beforeStart:start");
        var asyncEnd = At(order, "async.beforeStart:end");
        var asyncAfter = At(order, "async.afterStarted:start");
        var asyncAfterEnd = At(order, "async.afterStarted:end");
        var fastBefore = At(order, "fast.beforeStart");
        var fastAfter = At(order, "fast.afterStarted");

        Assert.True(asyncStart < asyncEnd);
        Assert.True(asyncEnd < asyncAfter);
        Assert.True(asyncEnd < fastAfter);
        Assert.True(fastBefore < fastAfter);
        Assert.True(fastBefore < asyncAfter);
        Assert.True(asyncAfter < asyncAfterEnd);
    }

    [HeadlessChromeFact]
    public async Task Hooks_receive_the_webview_arguments()
    {
        await using var page = await OpenAsync(server.ArgsId);
        var snap = await WaitForAsync(page, static s => Int(Object(s, "args"), "afterCount") == 1, "initializer arguments");
        var args = Object(snap, "args");

        Assert.Equal(0, Int(args, "beforeCount"));
        Assert.Equal(1, Int(args, "afterCount"));
        Assert.True(Flag(args, "afterIsBlazor"));
        Assert.True(Flag(args, "afterHasStart"));
    }

    [HeadlessChromeFact]
    public async Task A_throwing_hook_does_not_skip_the_sibling_beforeStart()
    {
        await using var page = await OpenAsync(server.ThrowingId);
        var snap = await WaitForAsync(
            page,
            static s => BootError(s)?.Contains("throwing initializer failed", StringComparison.Ordinal) == true
                && Strings(s["order"]).Contains("sibling.beforeStart")
                && Strings(s["order"]).Contains("throw.beforeStart"),
            "throwing initializer");
        var order = Strings(snap["order"]);

        Assert.DoesNotContain("sibling.afterStarted", order);
        Assert.DoesNotContain("throw.afterStarted", order);
        Assert.DoesNotContain(Ipc(snap), message => message.Contains("AttachPage", StringComparison.Ordinal));
    }

    [HeadlessChromeFact]
    public async Task A_missing_module_does_not_skip_the_sibling_beforeStart()
    {
        var missing = await server.GetAsync(server.MissingId, "missing.lib.module.js");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var page = await OpenAsync(server.MissingId);
        var snap = await WaitForAsync(
            page,
            static s => !string.IsNullOrEmpty(BootError(s)) && Strings(s["order"]).Contains("sibling.beforeStart"),
            "missing initializer");
        var order = Strings(snap["order"]);
        var error = BootError(snap);

        Assert.Contains("missing.lib.module.js", error);
        Assert.DoesNotContain("sibling.afterStarted", order);
        Assert.DoesNotContain(Ipc(snap), message => message.Contains("AttachPage", StringComparison.Ordinal));
    }

    [HeadlessChromeFact]
    public async Task Empty_manifest_runs_no_hooks()
    {
        await using var page = await OpenAsync(server.EmptyId);
        var snap = await WaitForAsync(page, static s => Attached(s) && BootError(s) is null, "empty manifest boot");

        Assert.Null(snap["baseline"]);
        Assert.Null(snap["order"]);
        Assert.Null(snap["other"]);
        Assert.Null(snap["nested"]);
    }

    [HeadlessChromeFact]
    public async Task Nested_module_resolves_under_the_rewritten_base_href()
    {
        await using var page = await OpenAsync(server.NestedId);
        var snap = await WaitForAsync(page, static s => Int(Object(s, "nested"), "afterStarted") == 1, "nested initializer");
        var nested = Object(snap, "nested");
        var url = new Uri(nested["url"]!.GetValue<string>());

        Assert.Equal(1, Int(nested, "beforeStart"));
        Assert.Equal(["beforeStart", "afterStarted"], Strings(nested["order"]));
        Assert.Equal($"/{server.NestedId}/_content/Lib/Nested.lib.module.js", url.AbsolutePath);
        Assert.Equal($"/{server.NestedId}/", new Uri(snap["baseHref"]!.GetValue<string>()).AbsolutePath);
        Assert.Contains(page.RequestUrls, request => new Uri(request).AbsolutePath == url.AbsolutePath);
    }

    [HeadlessChromeFact]
    public async Task Changed_initializer_is_picked_up_and_304_requires_the_new_etag()
    {
        const string path = "Changed.lib.module.js";
        var original = ModuleInitializerFiles.Read(path);
        var updated = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("const version = 1;", "const version = 2;", StringComparison.Ordinal));
        Assert.NotEqual(original, updated);
        Assert.NotEqual(Sha256(original), Sha256(updated));

        var first = await server.GetAsync(server.ChangedId, path);
        var firstBody = await first.Content.ReadAsByteArrayAsync();
        var firstEtag = Header(first, "ETag");
        Assert.Equal(original, firstBody);
        Assert.Equal(Sha256(original), Sha256(firstBody));
        Assert.Equal(ClientETag.GenerateETag(server.ChangedFiles.GetFileInfo(path)), firstEtag);

        var cached = await server.ConditionalGetAsync(server.ChangedId, path, firstEtag!);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        Assert.Empty(await cached.Content.ReadAsByteArrayAsync());

        await using var page = await OpenAsync(server.ChangedId);
        var loaded = await WaitForAsync(page, static s => Int(Object(s, "changed"), "version") == 1 && Int(Object(s, "changed"), "afterStarted") == 1, "changed v1");
        Assert.Equal(["beforeStart", "afterStarted"], Strings(Object(loaded, "changed")["order"]));

        server.ChangedFiles.Replace(path, updated);
        var second = await server.GetAsync(server.ChangedId, path);
        var secondBody = await second.Content.ReadAsByteArrayAsync();
        var secondEtag = Header(second, "ETag");
        Assert.Equal(updated, secondBody);
        Assert.Equal(Sha256(updated), Sha256(secondBody));
        Assert.NotEqual(firstEtag, secondEtag);
        Assert.Equal(ClientETag.GenerateETag(server.ChangedFiles.GetFileInfo(path)), secondEtag);

        var stale = await server.ConditionalGetAsync(server.ChangedId, path, firstEtag!);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Equal(updated, await stale.Content.ReadAsByteArrayAsync());

        var fresh = await server.ConditionalGetAsync(server.ChangedId, path, secondEtag!);
        Assert.Equal(HttpStatusCode.NotModified, fresh.StatusCode);
        Assert.Empty(await fresh.Content.ReadAsByteArrayAsync());

        await page.ReloadAsync();
        var reloaded = await WaitForAsync(page, static s => Int(Object(s, "changed"), "version") == 2 && Int(Object(s, "changed"), "afterStarted") == 1, "changed v2");
        var changed = Object(reloaded, "changed");
        Assert.Equal(1, Int(changed, "beforeStart"));
        Assert.Equal(["beforeStart", "afterStarted"], Strings(changed["order"]));
    }

    [HeadlessChromeFact]
    public async Task Browser_pages_do_not_see_another_clients_initializers()
    {
        await using var baseline = await OpenAsync(server.BaselineId);
        await using var other = await OpenAsync(server.OtherId);
        var baselineSnap = await WaitForAsync(baseline, BaselineReady, "baseline client");
        var otherSnap = await WaitForAsync(other, static s => Int(Object(s, "other"), "afterStarted") == 1, "other client");

        Assert.Null(baselineSnap["other"]);
        Assert.Null(otherSnap["baseline"]);
        Assert.Equal(1, Int(Object(otherSnap, "other"), "beforeStart"));
        Assert.DoesNotContain(other.RequestUrls, url => new Uri(url).AbsolutePath.Contains($"/{server.BaselineId}/", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline.RequestUrls, url => new Uri(url).AbsolutePath.Contains($"/{server.OtherId}/", StringComparison.Ordinal));
    }

    [HeadlessChromeFact]
    public async Task Object_shaped_manifest_does_not_run_the_named_module()
    {
        await using var page = await OpenAsync(server.ObjectManifestId);
        var snap = await WaitForAsync(page, static s => !string.IsNullOrEmpty(BootError(s)), "object manifest");

        Assert.Null(snap["baseline"]);
        Assert.DoesNotContain(Ipc(snap), message => message.Contains("AttachPage", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(BootError(snap)));
    }

    private async Task<ChromePage> OpenAsync(Guid id)
        => await (await server.ChromeAsync()).OpenAsync(server.PageUrl(id));

    private static async Task<JsonObject> WaitForAsync(ChromePage page, Func<JsonObject, bool> done, string why)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonObject? last = null;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                last = await page.SnapshotAsync();
                if (done(last))
                    return last;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(why + Environment.NewLine + last + Environment.NewLine + page.ConsoleText + Environment.NewLine + lastError);
    }

    private async Task<byte[]> Body(Guid id, string relative)
    {
        var response = await server.GetAsync(id, relative);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static bool BaselineReady(JsonObject snap)
        => Int(Object(snap, "baseline"), "beforeStart") == 1 && Int(Object(snap, "baseline"), "afterStarted") == 1;

    private static JsonObject Object(JsonObject snap, string name)
        => snap[name] as JsonObject ?? new JsonObject();

    private static int Int(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : -1;

    private static bool Flag(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static string[] Strings(JsonNode? node)
        => node is JsonArray array ? array.Select(item => item!.GetValue<string>()).ToArray() : [];

    private static string? BootError(JsonObject snap)
        => snap["probe"]?["bootError"] is JsonValue value && value.TryGetValue<string>(out var error) ? error : null;

    private static string[] Ipc(JsonObject snap) => Strings(snap["probe"]?["ipc"]);

    private static bool Attached(JsonObject snap)
        => Ipc(snap).Any(message => message.Contains("AttachPage", StringComparison.Ordinal));

    private static int At(string[] order, string name)
    {
        var index = Array.IndexOf(order, name);
        Assert.True(index >= 0, string.Join(" | ", order));
        return index;
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static string LocalContentType(string path)
        => ContentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
