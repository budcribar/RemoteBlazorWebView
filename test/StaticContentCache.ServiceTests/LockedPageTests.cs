using PeakSWC.RemoteWebView.Pages;

namespace StaticContentCache.Tests;

public class LockedPageTests
{
    [Fact]
    public void Locked_page_encodes_the_user_name()
    {
        var id = Guid.NewGuid();
        var html = LockedPage.Html("ada<b>m", id.ToString());

        Assert.DoesNotContain("ada<b>m", html);
        Assert.Contains("ada&lt;b&gt;m", html);
        Assert.Contains(id.ToString(), html);
    }
}
