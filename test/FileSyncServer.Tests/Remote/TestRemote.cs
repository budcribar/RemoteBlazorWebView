using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace FileSyncServer.Tests.Remote
{
    #region WPF
    [Collection("TestRemoteBlazorWpf")]
    public class TestRemoteBlazorWpf(ITestOutputHelper output) : BaseTestRemote<RemoteBlazorWpfFixture>(output) {
    }
    public class RemoteBlazorWpfFixture : BaseTestRemoteFixture { public RemoteBlazorWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfApp; } }

    [Collection("TestRemoteBlazorDebugWpf")]
    public class TestRemoteBlazorDebugWpf(ITestOutputHelper output) : BaseTestRemote<RemoteBlazorDebugWpfFixture>(output) {
    }
    public class RemoteBlazorDebugWpfFixture : BaseTestRemoteFixture { public RemoteBlazorDebugWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfDebugApp; } }
    [Collection("TestRemoteEmbeddedBlazorWpf")]
    public class TestRemoteEmbeddedBlazorWpf(ITestOutputHelper output) : BaseTestRemote<TestRemoteEmbeddedBlazorWpfFixture>(output) {
    }
    public class TestRemoteEmbeddedBlazorWpfFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfEmbeddedApp; } }
    #endregion

    #region Form
    [Collection("TestRemoteBlazorForm")]
    public class TestRemoteBlazorForm(ITestOutputHelper output) : BaseTestRemote<TestRemoteBlazorFormFixture>(output) {
    }
    public class TestRemoteBlazorFormFixture : BaseTestRemoteFixture { public TestRemoteBlazorFormFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsApp; } }

    [Collection("TestRemoteBlazorWinFormsDebug")]
    public class TestRemoteBlazorWinFormsDebug(ITestOutputHelper output) : BaseTestRemote<TestRemoteBlazorWinFormsDebugFixture>(output) {
    }
    public class TestRemoteBlazorWinFormsDebugFixture : BaseTestRemoteFixture { public TestRemoteBlazorWinFormsDebugFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsDebugApp; } }

    [Collection("TestRemoteEmbeddedBlazorForm")]
    public class TestRemoteEmbeddedBlazorForm(ITestOutputHelper output) : BaseTestRemote<TestRemoteEmbeddedBlazorFormFixture>(output) {
    }
    public class TestRemoteEmbeddedBlazorFormFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorFormFixture() { ClientExecutablePath = Utilities.StartRemoteEmbeddedBlazorWinFormsApp; } }
    #endregion

    #region Photino

    [Collection("TestRemoteBlazorWebView")]
    public class TestRemoteBlazorWebView(ITestOutputHelper output) : BaseTestRemote<TestRemoteBlazorWebViewFixture>(output) {
    }
    public class TestRemoteBlazorWebViewFixture : BaseTestRemoteFixture { public TestRemoteBlazorWebViewFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWebViewApp; } }

    [Collection("TestRemoteEmbeddedBlazorWebView")]
    public class TestRemoteEmbeddedBlazorWebView(ITestOutputHelper output) : BaseTestRemote<TestRemoteEmbeddedBlazorWebViewFixture>(output) {
    }
    public class TestRemoteEmbeddedBlazorWebViewFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorWebViewFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWebViewEmbeddedApp; } }



    #endregion

}
