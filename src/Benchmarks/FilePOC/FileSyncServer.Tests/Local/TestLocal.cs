
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    [Collection("TestLocalBlazorForm")]
    public class TestLocalBlazorForm : BaseTestClicks<LocalBlazorFormFixture> { public TestLocalBlazorForm( ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorFormFixture : BaseTestFixture { public LocalBlazorFormFixture() { ClientExecutablePath = Utilities.BlazorWinFormsAppExe(); } }

    [Collection("TestLocalEmbeddedBlazorForm")]
    public class TestLocalEmbeddedBlazorForm : BaseTestClicks<LocalEmbeddedBlazorFormFixture> { public TestLocalEmbeddedBlazorForm(ITestOutputHelper output) : base(output) { } }
    public class LocalEmbeddedBlazorFormFixture : BaseTestFixture { public LocalEmbeddedBlazorFormFixture() { ClientExecutablePath = Utilities.BlazorWinFormsEmbeddedAppExe(); } }

    //[Collection("TestLocalBlazorWebView")]
    //public class TestLocalBlazorWebView : BaseTestClicks<LocalBlazorWebViewFixture> { public TestLocalBlazorWebView(ITestOutputHelper output) : base(output) { } }
    //public class LocalBlazorWebViewFixture : BaseTestFixture { public LocalBlazorWebViewFixture() { AppExecutablePath = Utilities.BlazorWebViewAppExe(); } }

    [Collection("TestLocalBlazorWpf")]
    public class TestLocalBlazorWpf : BaseTestClicks<LocalBlazorWpfFixture> { public TestLocalBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorWpfFixture : BaseTestFixture { public LocalBlazorWpfFixture() { ClientExecutablePath = Utilities.BlazorWpfAppExe(); } }

    [Collection("TestLocalEmbeddedBlazorWpf")]
    public class TestLocalEmbeddedBlazorWpf : BaseTestClicks<LocalBlazorWpfFixture> { public TestLocalEmbeddedBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class LocalEmbeddedBlazorWpfFixture : BaseTestFixture { public LocalEmbeddedBlazorWpfFixture() { ClientExecutablePath = Utilities.BlazorWpfAppEmbeddedExe(); } }

}
