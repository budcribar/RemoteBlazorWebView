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
            var physical = Assert.IsType<PhysicalFileProvider>(provider);
            // The supplied directory is used even when it does not contain the host page.
            // CreateFileProvider does not search beside the process executable.
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)),
                Path.TrimEndingDirectorySeparator(physical.Root));
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
