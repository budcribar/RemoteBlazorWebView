// BaseTestClicks.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace FileSyncServer.Tests.Local
{
    /// <summary>
    /// Abstract base class containing shared test methods like TestClicks.
    /// Derived classes must supply the executable path via the fixture.
    /// </summary>
    public abstract class BaseTestClicks<T>(ITestOutputHelper output) : IAsyncLifetime where T : BaseTestFixture, new()
    {
        protected readonly T Fixture = new();
        protected readonly ITestOutputHelper Output = output;

        // Initialize and Dispose methods delegate to the fixture
        public virtual async Task InitializeAsync()
        {
            await Fixture.InitializeAsync();
            // Additional shared initialization if needed
        }

        public virtual async Task DisposeAsync()
        {
            await Fixture.DisposeAsync();
            // Additional shared cleanup if needed
        }

        /// <summary>
        /// Shared TestClicks method that can be used across different test classes.
        /// </summary>
        /// <param name="numClicks">Number of clicks to perform.</param>
        [Theory]
        //[InlineData(10)]
        [InlineData(100)]
        public virtual async Task TestClicks(int numClicks)
        {
            Output.WriteLine($"Starting TestClicks with {numClicks} clicks.");

            try
            {
                // Photino's WebView viewport on the hosted runner stays under the Blazor
                // sidebar breakpoint, so the link is in the DOM and display:none until
                // the navbar toggler opens it.
                await ClickNavAsync("Counter");
                Output.WriteLine("Clicked on the 'Counter' link.");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error clicking 'Counter' link: {ex.Message}");
                throw;
            }

            // Wait for the Counter component to load
            await Fixture.Page.WaitForSelectorAsync("h1:text('Counter')");

            // Locate the increment button and the paragraph displaying the count
            var incrementButton = Fixture.Page.Locator("button:has-text('Click me')");
            var countParagraph = Fixture.Page.Locator("p");

            // Ensure elements are visible
            await incrementButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await countParagraph.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // Perform clicks without delay
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < numClicks; i++)
            {
                await incrementButton.ClickAsync();
            }
            sw.Stop();
            Output.WriteLine($"Clicked {numClicks} times in {sw.Elapsed.TotalSeconds} seconds.");

            // Get the count value
            await Assertions.Expect(countParagraph).ToContainTextAsync($"{numClicks}", new LocatorAssertionsToContainTextOptions { Timeout = 8000 });

            // Navigate back to the Home page
            try
            {
                await ClickNavAsync("Home");
                Output.WriteLine("Navigated back to the 'Home' page.");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error navigating back to 'Home' page: {ex.Message}");
                throw;
            }
        }

        private async Task ClickNavAsync(string text)
        {
            // Diagnostics only. The click sequence below is unchanged.
            var before = await DescribePageAsync(text);
            Output.WriteLine("before-click " + text);
            Output.WriteLine(before);
            Console.WriteLine("before-click " + text);
            Console.WriteLine(before);

            var link = Fixture.Page.Locator("a.nav-link", new PageLocatorOptions { HasText = text });
            if (!await link.IsVisibleAsync())
            {
                var toggler = Fixture.Page.Locator(".navbar-toggler");
                var togglerVisible = await toggler.IsVisibleAsync();
                Output.WriteLine($"navbar-toggler visible={togglerVisible} before clicking '{text}'.");
                if (togglerVisible)
                {
                    await toggler.ClickAsync();
                    Output.WriteLine($"Opened the navbar before clicking '{text}'.");
                }
            }
            try
            {
                await link.ClickAsync();
            }
            catch (Exception ex)
            {
                var after = await DescribePageAsync(text);
                Output.WriteLine("click-failed " + text + " " + ex.GetType().Name);
                Output.WriteLine(after);
                Console.WriteLine("click-failed " + text);
                Console.WriteLine(after);
                throw;
            }
        }

        private async Task<string> DescribePageAsync(string linkText)
        {
            var native = NativeWindowProbe.Describe(Fixture.AppProcess);
            string page;
            try
            {
                page = await Fixture.Page.EvaluateAsync<string>(
                    @"(linkText) => {
                        const line = (label, el) => {
                            if (!el) return label + ': missing';
                            const s = getComputedStyle(el);
                            const r = el.getBoundingClientRect();
                            const cls = el.className && el.className.toString ? el.className.toString() : '';
                            return label + ': display=' + s.display
                                + ' visibility=' + s.visibility
                                + ' opacity=' + s.opacity
                                + ' rect=' + r.x.toFixed(1) + ',' + r.y.toFixed(1) + ' ' + r.width.toFixed(1) + 'x' + r.height.toFixed(1)
                                + ' class=' + cls;
                        };
                        const link = [...document.querySelectorAll('a')].find(a => (a.textContent || '').trim() === linkText) || null;
                        const narrow = window.matchMedia('(max-width: 640.98px)').matches;
                        const wide = window.matchMedia('(min-width: 641px)').matches;
                        const rows = [
                            'url=' + location.href,
                            'title=' + document.title,
                            'readyState=' + document.readyState,
                            'inner=' + window.innerWidth + 'x' + window.innerHeight,
                            'outer=' + window.outerWidth + 'x' + window.outerHeight,
                            'devicePixelRatio=' + window.devicePixelRatio,
                            'documentElement.client=' + document.documentElement.clientWidth + 'x' + document.documentElement.clientHeight,
                            'screen=' + screen.width + 'x' + screen.height,
                            'screen.avail=' + screen.availWidth + 'x' + screen.availHeight,
                            'matchMedia max-width 640.98px=' + narrow,
                            'matchMedia min-width 641px=' + wide,
                            line('link', link),
                            line('navbar-toggler', document.querySelector('.navbar-toggler')),
                            line('blazor-error-ui', document.querySelector('#blazor-error-ui'))
                        ];
                        const wanted = ['nav-scrollable', 'navbar-collapse', 'top-row', 'collapse', 'sidebar'];
                        for (const name of wanted) {
                            const el = link ? link.closest('.' + name) : document.querySelector('.' + name);
                            rows.push(line(name, el));
                        }
                        const text = (document.body && document.body.innerText) ? document.body.innerText.replace(/\s+/g, ' ').slice(0, 400) : '';
                        rows.push('body=' + text);
                        return rows.join('\n');
                    }",
                    linkText);
            }
            catch (Exception ex)
            {
                page = "page-eval-failed: " + ex.Message;
            }

            var exe = Fixture.AppProcess?.StartInfo.FileName ?? "";
            return $"exe={exe}{Environment.NewLine}{native}{Environment.NewLine}{page}";
        }
    }

    internal static class NativeWindowProbe
    {
        internal static string Describe(Process? process)
        {
            if (process == null)
                return "native-window: process=null";
            if (!OperatingSystem.IsWindows())
                return "native-window: not-windows";

            try
            {
                process.Refresh();
            }
            catch (Exception ex)
            {
                return "native-window: refresh-failed " + ex.Message;
            }

            var rows = new List<string>();
            var pid = (uint)process.Id;
            EnumWindowsProc callback = (hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var windowPid);
                if (windowPid != pid || !IsWindowVisible(hwnd))
                    return true;

                GetWindowRect(hwnd, out var window);
                GetClientRect(hwnd, out var client);
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                rows.Add(
                    $"hwnd=0x{hwnd.ToInt64():X} title='{title}' iconic={IsIconic(hwnd)} zoomed={IsZoomed(hwnd)} " +
                    $"window={window.Right - window.Left}x{window.Bottom - window.Top}@({window.Left},{window.Top}) " +
                    $"client={client.Right}x{client.Bottom} dpi={GetDpiForWindow(hwnd)}");
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            if (rows.Count == 0)
                return $"native-window: pid={pid} main=0x{process.MainWindowHandle.ToInt64():X} visible=none";

            return "native-window: " + string.Join(" | ", rows);
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
