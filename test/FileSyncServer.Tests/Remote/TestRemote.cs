using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    #region WPF
    [Collection("TestRemoteBlazorWpf")]
    public class TestRemoteBlazorWpf : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class RemoteBlazorWpfFixture : BaseTestRemoteFixture { public RemoteBlazorWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfApp; } }

    [Collection("TestRemoteBlazorDebugWpf")]
    public class TestRemoteBlazorDebugWpf : BaseTestRemote<RemoteBlazorDebugWpfFixture> { public TestRemoteBlazorDebugWpf(ITestOutputHelper output) : base(output) { } }
    public class RemoteBlazorDebugWpfFixture : BaseTestRemoteFixture { public RemoteBlazorDebugWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfDebugApp; } }
    [Collection("TestRemoteEmbeddedBlazorWpf")]
    public class TestRemoteEmbeddedBlazorWpf : BaseTestRemote<TestRemoteEmbeddedBlazorWpfFixture> { public TestRemoteEmbeddedBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteEmbeddedBlazorWpfFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfEmbeddedApp; } }
    #endregion

    #region Form
    [Collection("TestRemoteBlazorForm")]
    public class TestRemoteBlazorForm : BaseTestRemote<TestRemoteBlazorFormFixture> { public TestRemoteBlazorForm(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteBlazorFormFixture : BaseTestRemoteFixture { public TestRemoteBlazorFormFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsApp; } }

    [Collection("TestRemoteBlazorWinFormsDebug")]
    public class TestRemoteBlazorWinFormsDebug : BaseTestRemote<TestRemoteBlazorWinFormsDebugFixture> { public TestRemoteBlazorWinFormsDebug(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteBlazorWinFormsDebugFixture : BaseTestRemoteFixture { public TestRemoteBlazorWinFormsDebugFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsDebugApp; } }

    [Collection("TestRemoteEmbeddedBlazorForm")]
    public class TestRemoteEmbeddedBlazorForm : BaseTestRemote<TestRemoteEmbeddedBlazorFormFixture> { public TestRemoteEmbeddedBlazorForm(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteEmbeddedBlazorFormFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorFormFixture() { ClientExecutablePath = Utilities.StartRemoteEmbeddedBlazorWinFormsApp; } }
    #endregion

    #region Photino

    [Collection("TestRemoteBlazorWebView")]
    public class TestRemoteBlazorWebView : BaseTestRemote<TestRemoteBlazorWebViewFixture> { public TestRemoteBlazorWebView(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteBlazorWebViewFixture : BaseTestRemoteFixture { public TestRemoteBlazorWebViewFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWebViewApp; } }

    [Collection("TestRemoteEmbeddedBlazorWebView")]
    public class TestRemoteEmbeddedBlazorWebView : BaseTestRemote<TestRemoteEmbeddedBlazorWebViewFixture> { public TestRemoteEmbeddedBlazorWebView(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteEmbeddedBlazorWebViewFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorWebViewFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWebViewEmbeddedApp; } }



    #endregion

}
