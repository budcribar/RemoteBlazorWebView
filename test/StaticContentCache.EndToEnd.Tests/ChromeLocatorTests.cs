namespace StaticContentCache.EndToEnd.Tests;

public class ChromeLocatorTests
{
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

    private static void WriteFake(DirectoryInfo root, string install, string relative, string contents)
    {
        var path = Path.Combine(root.FullName, install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
