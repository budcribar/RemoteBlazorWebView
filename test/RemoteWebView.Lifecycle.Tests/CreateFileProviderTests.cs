extern alias client;

using Microsoft.Extensions.FileProviders;
using ClientRemote = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

public class CreateFileProviderTests
{
    [Fact]
    public void Missing_content_root_does_not_use_PhysicalFileProvider()
    {
        var missing = Path.Combine(Path.GetTempPath(), "rbw-" + Guid.NewGuid().ToString("n"));
        Assert.False(Directory.Exists(missing));

        var provider = ClientRemote.RemoteWebView.CreateFileProvider(missing, @"wwwroot\index.html");

        Assert.IsNotType<PhysicalFileProvider>(provider);
    }

    [Fact]
    public void Existing_content_root_uses_PhysicalFileProvider()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rbw-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var provider = ClientRemote.RemoteWebView.CreateFileProvider(dir, @"wwwroot\index.html");
            Assert.IsType<PhysicalFileProvider>(provider);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
