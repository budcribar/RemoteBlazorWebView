using System.Reflection;
using System.Text;

namespace StaticContentCache.EndToEnd.Tests;

public class ChromeLocatorTests
{
    [Fact]
    public void Chrome_output_reader_starts_off_the_thread_pool_before_drain_returns()
    {
        var pool = ChromeSession.StartPipeReader(new StringReader(""), new StringBuilder());
        Assert.False(pool);
    }

    [Fact]
    public async Task Debugger_wait_retries_after_the_http_client_timeout()
    {
        var attempts = 0;
        await ChromeSession.WaitForHttpAsync(
            () =>
            {
                attempts++;
                if (attempts == 1)
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing.");
                return Task.FromResult(true);
            },
            () => false,
            TimeSpan.FromSeconds(2),
            () => "exited",
            () => "timed out");

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Chrome_module_classes_share_one_collection()
    {
        var names = new[]
        {
            typeof(BaselineModuleRunTests),
            typeof(ModuleInitializerContractTests),
            typeof(ModuleInitializerParityTests)
        }.Select(CollectionName).ToArray();

        Assert.All(names, name => Assert.Equal("HeadlessChrome", name));
    }

    private static string? CollectionName(Type type)
    {
        var data = type.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType == typeof(CollectionAttribute));
        return data is { ConstructorArguments.Count: > 0 }
            ? data.ConstructorArguments[0].Value as string
            : null;
    }

    [Fact]
    public void Windows_registry_is_local_app_data_ms_playwright()
    {
        var roots = ChromeSession.PlaywrightRegistryRoots(
            browsersPath: null,
            localAppData: @"C:\Users\runneradmin\AppData\Local",
            home: @"C:\Users\runneradmin",
            xdgCacheHome: null,
            windows: true).ToArray();

        Assert.Equal([Path.Combine(@"C:\Users\runneradmin\AppData\Local", "ms-playwright")], roots);
    }

    [Fact]
    public void Linux_registry_includes_the_cache_directory()
    {
        var roots = ChromeSession.PlaywrightRegistryRoots(
            browsersPath: null,
            localAppData: null,
            home: "/home/runner",
            xdgCacheHome: "/tmp/xdg-cache",
            windows: false).ToArray();

        Assert.Equal(
            [Path.Combine("/tmp/xdg-cache", "ms-playwright"), Path.Combine("/home/runner", ".cache", "ms-playwright")],
            roots);
    }

    [Fact]
    public void Playwright_browsers_path_replaces_the_default_registry()
    {
        var browsers = Path.Combine(Path.GetTempPath(), "explicit-browsers");
        var roots = ChromeSession.PlaywrightRegistryRoots(
            browsersPath: browsers,
            localAppData: @"C:\Users\runneradmin\AppData\Local",
            home: @"C:\Users\runneradmin",
            xdgCacheHome: null,
            windows: true).ToArray();

        Assert.Equal([browsers], roots);
        Assert.Empty(ChromeSession.PlaywrightRegistryRoots("0", @"C:\Users\runneradmin\AppData\Local", null, null, windows: true));
    }

    [Fact]
    public void Newest_chromium_install_wins_and_the_headless_shell_is_ignored()
    {
        var root = Directory.CreateTempSubdirectory("playwright-chrome-");
        try
        {
            WriteFake(root, "chromium-10", Path.Combine("chrome-linux64", "chrome"), "older");
            WriteFake(root, "chromium-1243", Path.Combine("chrome-win64", "chrome.exe"), "current");
            WriteFake(root, "chromium_headless_shell-1243", Path.Combine("chrome-headless-shell-win64", "chrome-headless-shell.exe"), "shell");
            WriteFake(root, "firefox-1543", Path.Combine("firefox", "firefox.exe"), "firefox");

            var found = ChromeSession.FindPlaywrightChrome(root.FullName);

            Assert.Equal(Path.Combine(root.FullName, "chromium-1243", "chrome-win64", "chrome.exe"), found);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Shared_debug_port_wrapper_is_skipped_for_an_older_real_binary()
    {
        var root = Directory.CreateTempSubdirectory("playwright-wrapper-");
        try
        {
            WriteFake(root, "chromium-20", Path.Combine("chrome-win64", "chrome.exe"), "exec --remote-debugging-port=9222");
            WriteFake(root, "chromium-11", Path.Combine("chrome-linux64", "chrome"), "chrome");

            var found = ChromeSession.FindPlaywrightChrome(root.FullName);

            Assert.Equal(Path.Combine(root.FullName, "chromium-11", "chrome-linux64", "chrome"), found);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_registry_returns_null()
    {
        Assert.Null(ChromeSession.FindPlaywrightChrome(Path.Combine(Path.GetTempPath(), "no-such-playwright-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Chrome_output_reader_enters_blocking_read_and_drains_when_released()
    {
        using var reader = new BlockingTextReader("drained-output");
        var sink = new StringBuilder();

        var pool = ChromeSession.StartPipeReader(reader, sink);

        Assert.False(pool);
        Assert.True(reader.EnteredRead.Wait(TimeSpan.FromSeconds(2)));
        reader.ReleaseRead.Set();
        Assert.True(reader.Completed.Wait(TimeSpan.FromSeconds(2)));
        lock (sink)
            Assert.Equal("drained-output", sink.ToString());
    }

    [Fact]
    public void Installed_candidates_include_microsoft_edge_paths()
    {
        var candidates = ChromeSession.InstalledCandidates().ToArray();

        Assert.Contains(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", candidates);
        Assert.Contains(@"C:\Program Files\Microsoft\Edge\Application\msedge.exe", candidates);
        Assert.Contains("/usr/bin/microsoft-edge-stable", candidates);
    }

    private static void WriteFake(DirectoryInfo root, string install, string relative, string contents)
    {
        var path = Path.Combine(root.FullName, install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private sealed class BlockingTextReader(string payload) : TextReader
    {
        private bool _served;
        public ManualResetEventSlim EnteredRead { get; } = new(false);
        public ManualResetEventSlim ReleaseRead { get; } = new(false);
        public ManualResetEventSlim Completed { get; } = new(false);

        public override int Read(char[] buffer, int index, int count)
        {
            if (!_served)
            {
                EnteredRead.Set();
                if (!ReleaseRead.Wait(TimeSpan.FromSeconds(5)))
                    throw new IOException("Timed out waiting for release.");
                _served = true;
                var length = Math.Min(count, payload.Length);
                payload.CopyTo(0, buffer, index, length);
                return length;
            }

            Completed.Set();
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseRead.Set();
                EnteredRead.Dispose();
                ReleaseRead.Dispose();
                Completed.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
