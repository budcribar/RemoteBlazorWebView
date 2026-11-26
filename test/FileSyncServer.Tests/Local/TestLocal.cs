
using FileSyncServer.Tests.Local;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace FileSyncServer.Tests.Local
{
    #region Form
    [Collection("TestLocalBlazorForm")]
    [Trait("Category", "Slow")]
    public class TestLocalBlazorForm : BaseTestClicks<LocalBlazorFormFixture> { public TestLocalBlazorForm( ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorFormFixture : BaseTestFixture { public LocalBlazorFormFixture() { ClientExecutablePath = Utilities.BlazorWinFormsAppExe(); } }

    [Collection("TestLocalEmbeddedBlazorForm")]
    [Trait("Category", "Slow")]
    public class TestLocalEmbeddedBlazorForm : BaseTestClicks<LocalEmbeddedBlazorFormFixture> { public TestLocalEmbeddedBlazorForm(ITestOutputHelper output) : base(output) { } }
    public class LocalEmbeddedBlazorFormFixture : BaseTestFixture { public LocalEmbeddedBlazorFormFixture() { ClientExecutablePath = Utilities.BlazorWinFormsEmbeddedAppExe(); } }
    #endregion

    #region Photino
    [Collection("TestLocalBlazorWebView")]
    [Trait("Category", "Slow")]
    public class TestLocalBlazorWebView : BaseTestClicks<LocalBlazorWebViewFixture> { public TestLocalBlazorWebView(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorWebViewFixture : BaseTestFixture { public LocalBlazorWebViewFixture() { ClientExecutablePath = Utilities.BlazorWebViewAppExe(); } }

    [Collection("TestLocalBlazorWebViewDebug")]
    [Trait("Category", "Slow")]
    public class TestLocalBlazorWebViewDebug : BaseTestClicks<TestLocalBlazorWebViewDebugFixture> { public TestLocalBlazorWebViewDebug(ITestOutputHelper output) : base(output) { } }
    public class TestLocalBlazorWebViewDebugFixture : BaseTestFixture { public TestLocalBlazorWebViewDebugFixture() { ClientExecutablePath = Utilities.BlazorWebViewDebugAppExe(); } }

    [Collection("TestLocalEmbeddedBlazorWebView")]
    [Trait("Category", "Slow")]
    public class TestLocalEmbeddedBlazorWebView : BaseTestClicks<TestLocalEmbeddedBlazorWebViewFixture> { public TestLocalEmbeddedBlazorWebView(ITestOutputHelper output) : base(output) { } }
    public class TestLocalEmbeddedBlazorWebViewFixture : BaseTestFixture { public TestLocalEmbeddedBlazorWebViewFixture() { ClientExecutablePath = Utilities.BlazorWebViewAppEmbeddedExe(); } }

    #endregion

    #region Wpf
    [Collection("TestLocalBlazorWpf")]
    [Trait("Category", "Slow")]
    public class TestLocalBlazorWpf : BaseTestClicks<LocalBlazorWpfFixture> { public TestLocalBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorWpfFixture : BaseTestFixture { public LocalBlazorWpfFixture() { ClientExecutablePath = Utilities.BlazorWpfAppExe(); } }

    [Collection("TestLocalEmbeddedBlazorWpf")]
    [Trait("Category", "Slow")]
    public class TestLocalEmbeddedBlazorWpf : BaseTestClicks<LocalEmbeddedBlazorWpfFixture> { public TestLocalEmbeddedBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class LocalEmbeddedBlazorWpfFixture : BaseTestFixture { public LocalEmbeddedBlazorWpfFixture() { ClientExecutablePath = Utilities.BlazorWpfAppEmbeddedExe(); } }
    #endregion
}
