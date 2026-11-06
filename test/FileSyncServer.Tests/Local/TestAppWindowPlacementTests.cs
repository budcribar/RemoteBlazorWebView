namespace FileSyncServer.Tests.Local;

public class TestAppWindowPlacementTests
{
    [Fact]
    public void Measured_photino_window_grows_above_the_sidebar_breakpoint_and_stays_on_the_work_area()
    {
        // Log: window 400x360 at (180,40), client 387x324, CSS 388x325, screen.avail 1536x864, dpi 240.
        var placement = TestAppWindowPlacement.Plan(
            workLeft: 0, workTop: 0, workWidth: 1536, workHeight: 864,
            currentX: 180, currentY: 40, currentWidth: 400, currentHeight: 360,
            clientWidth: 387, clientHeight: 324,
            cssWidth: 388, cssHeight: 325);

        Assert.True(placement.Width > 400);
        Assert.True(placement.Height > 360);
        Assert.InRange(placement.X, 0, 1536 - placement.Width);
        Assert.InRange(placement.Y, 0, 864 - placement.Height);
        var clientWidth = placement.Width - (400 - 387);
        var cssWidth = clientWidth * 388 / 387;
        Assert.True(cssWidth >= TestAppWindowPlacement.SidebarCssWidth);
    }

    [Fact]
    public void High_dpi_where_css_pixels_are_smaller_than_client_pixels_does_not_leave_the_work_area()
    {
        var placement = TestAppWindowPlacement.Plan(
            workLeft: 0, workTop: 0, workWidth: 1536, workHeight: 864,
            currentX: 0, currentY: 0, currentWidth: 400, currentHeight: 360,
            clientWidth: 1000, clientHeight: 800,
            cssWidth: 400, cssHeight: 320);

        Assert.InRange(placement.X, 0, 1536);
        Assert.InRange(placement.Y, 0, 864);
        Assert.True(placement.X + placement.Width <= 1536);
        Assert.True(placement.Y + placement.Height <= 864);
    }

    [Fact]
    public void A_window_already_wide_enough_is_not_shrunk()
    {
        var placement = TestAppWindowPlacement.Plan(
            workLeft: 0, workTop: 0, workWidth: 1920, workHeight: 1080,
            currentX: 10, currentY: 10, currentWidth: 1440, currentHeight: 900,
            clientWidth: 1400, clientHeight: 860,
            cssWidth: 1400, cssHeight: 860);

        Assert.Equal(1440, placement.Width);
        Assert.Equal(900, placement.Height);
        Assert.Equal(10, placement.X);
        Assert.Equal(10, placement.Y);
    }

    [Fact]
    public void A_388_css_pixel_viewport_is_the_hamburger_layout()
    {
        Assert.True(TestAppWindowPlacement.UsesHamburgerMenu(388, true));
        Assert.False(TestAppWindowPlacement.UsesHamburgerMenu(1200, false));
    }
}
