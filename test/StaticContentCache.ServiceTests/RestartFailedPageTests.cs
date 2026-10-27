using PeakSWC.RemoteWebView.Pages;

namespace StaticContentCache.Tests;

public class RestartFailedPageTests
{
    [Fact]
    public void Failure_page_encodes_the_process_and_host_names()
    {
        var html = RestartFailedPage.Html("app<b>x", 42, "host\"y");

        Assert.DoesNotContain("app<b>x", html);
        Assert.Contains("app&lt;b&gt;x", html);
        Assert.DoesNotContain("host\"y", html);
        Assert.Contains("host&quot;y", html);
        Assert.Contains("42", html);
    }
}
