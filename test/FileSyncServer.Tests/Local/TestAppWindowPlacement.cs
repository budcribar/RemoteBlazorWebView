using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Playwright;

namespace FileSyncServer.Tests.Local;

/// <summary>
/// Chooses a top-level test window that stays on the monitor work area and is
/// wide enough for the Blazor sidebar (the layout collapses below 641 CSS pixels).
/// Client pixels and CSS pixels are not assumed equal: the caller passes the
/// measured ratio. Nothing here changes the machine DPI or resolution.
/// </summary>
public static class TestAppWindowPlacement
{
    public const int SidebarCssWidth = 641;
    public const int TargetCssWidth = 1200;
    public const int TargetCssHeight = 800;
    public const int Margin = 8;

    public readonly record struct Placement(int X, int Y, int Width, int Height);

    public static Placement Plan(
        int workLeft,
        int workTop,
        int workWidth,
        int workHeight,
        int currentX,
        int currentY,
        int currentWidth,
        int currentHeight,
        int clientWidth,
        int clientHeight,
        int cssWidth,
        int cssHeight)
    {
        var frameWidth = Math.Max(0, currentWidth - Math.Max(0, clientWidth));
        var frameHeight = Math.Max(0, currentHeight - Math.Max(0, clientHeight));
        var cssPerClientNumerator = cssWidth > 0 && clientWidth > 0 ? cssWidth : 1;
        var cssPerClientDenominator = cssWidth > 0 && clientWidth > 0 ? clientWidth : 1;

        var alreadyWide = cssWidth >= TargetCssWidth && cssHeight >= TargetCssHeight;
        var outerWidth = alreadyWide
            ? currentWidth
            : DesiredOuter(TargetCssWidth, cssPerClientNumerator, cssPerClientDenominator, frameWidth);
        var outerHeight = alreadyWide
            ? currentHeight
            : DesiredOuter(TargetCssHeight, cssPerClientNumerator, cssPerClientDenominator, frameHeight);

        var maxWidth = Math.Max(1, workWidth - Margin);
        var maxHeight = Math.Max(1, workHeight - Margin);
        if (outerWidth > maxWidth)
            outerWidth = maxWidth;
        if (outerHeight > maxHeight)
            outerHeight = maxHeight;

        var x = currentX < workLeft ? workLeft : currentX;
        var y = currentY < workTop ? workTop : currentY;
        if (x + outerWidth > workLeft + workWidth)
            x = workLeft + Math.Max(0, workWidth - outerWidth);
        if (y + outerHeight > workTop + workHeight)
            y = workTop + Math.Max(0, workHeight - outerHeight);

        return new Placement(x, y, outerWidth, outerHeight);
    }

    public static bool UsesHamburgerMenu(int cssWidth, bool narrowMedia) =>
        narrowMedia || (cssWidth > 0 && cssWidth < SidebarCssWidth);

    public static async Task FitAsync(Process? process, IPage? page)
    {
        if (!OperatingSystem.IsWindows() || process == null)
            return;

        var hwnd = IntPtr.Zero;
        for (var attempt = 0; attempt < 50 && hwnd == IntPtr.Zero; attempt++)
        {
            hwnd = FindVisibleWindow(process.Id);
            if (hwnd == IntPtr.Zero)
                await Task.Delay(100);
        }
        if (hwnd == IntPtr.Zero)
            return;

        var css = await WaitForRenderedCssAsync(page);
        for (var pass = 0; pass < 2; pass++)
        {
            var alreadyWide = css.Width >= SidebarCssWidth && !css.Narrow;
            if (alreadyWide || (pass > 0 && !UsesHamburgerMenu(css.Width, css.Narrow)))
                break;
            Move(hwnd, css.Width, css.Height);
            css = await WaitForViewportToSettleAsync(page);
        }

        Console.WriteLine(
            $"test-window: css={css.Width}x{css.Height} narrow={css.Narrow} hamburger={UsesHamburgerMenu(css.Width, css.Narrow)}");
    }

    private static void Move(IntPtr hwnd, int cssWidth, int cssHeight)
    {
        var monitor = MonitorFromWindow(hwnd, DefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;
        if (!GetWindowRect(hwnd, out var window) || !GetClientRect(hwnd, out var client))
            return;

        var placement = Plan(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left,
            info.rcWork.Bottom - info.rcWork.Top,
            window.Left,
            window.Top,
            window.Right - window.Left,
            window.Bottom - window.Top,
            client.Right,
            client.Bottom,
            cssWidth,
            cssHeight);
        SetWindowPos(hwnd, IntPtr.Zero, placement.X, placement.Y, placement.Width, placement.Height, NoZOrder | NoActivate);
    }

    private readonly record struct CssViewport(int Width, int Height, bool Narrow);

    private static async Task<CssViewport> WaitForRenderedCssAsync(IPage? page)
    {
        if (page == null)
            return new CssViewport(0, 0, false);
        try
        {
            await page.WaitForFunctionAsync(
                "() => { const t = document.body && document.body.innerText ? document.body.innerText.trim() : ''; return t.length > 0 && !t.startsWith('Loading'); }",
                null,
                new PageWaitForFunctionOptions { Timeout = 30000 });
            var size = await page.EvaluateAsync<int[]>(
                "() => [window.innerWidth, window.innerHeight, window.matchMedia('(max-width: 640.98px)').matches ? 1 : 0]");
            return size is { Length: 3 }
                ? new CssViewport(size[0], size[1], size[2] == 1)
                : new CssViewport(0, 0, false);
        }
        catch
        {
            return new CssViewport(0, 0, false);
        }
    }

    private static async Task<CssViewport> WaitForViewportToSettleAsync(IPage? page)
    {
        var css = await ReadCssAsync(page);
        for (var attempt = 0; attempt < 20 && UsesHamburgerMenu(css.Width, css.Narrow); attempt++)
        {
            await Task.Delay(100);
            var next = await ReadCssAsync(page);
            if (next.Width == css.Width && next.Height == css.Height && next.Narrow == css.Narrow && attempt >= 4)
                return next;
            css = next;
        }
        return css;
    }

    private static async Task<CssViewport> ReadCssAsync(IPage? page)
    {
        if (page == null)
            return new CssViewport(0, 0, false);
        try
        {
            var size = await page.EvaluateAsync<int[]>(
                "() => [window.innerWidth, window.innerHeight, window.matchMedia('(max-width: 640.98px)').matches ? 1 : 0]");
            return size is { Length: 3 }
                ? new CssViewport(size[0], size[1], size[2] == 1)
                : new CssViewport(0, 0, false);
        }
        catch
        {
            return new CssViewport(0, 0, false);
        }
    }

    private static IntPtr FindVisibleWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        var area = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != (uint)processId || !IsWindowVisible(hwnd))
                return true;
            if (!GetWindowRect(hwnd, out var rect))
                return true;
            var next = Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (next >= area)
            {
                area = next;
                found = hwnd;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private const uint DefaultToNearest = 2;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static int DesiredOuter(int targetCss, int cssNumerator, int cssDenominator, int frame)
    {
        var desiredClient = (targetCss * cssDenominator + cssNumerator - 1) / cssNumerator;
        return Math.Max(1, desiredClient + frame);
    }
}
