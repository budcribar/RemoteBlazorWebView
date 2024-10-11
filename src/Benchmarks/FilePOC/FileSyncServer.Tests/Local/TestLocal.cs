
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    [Collection("TestLocalBlazorForm")]
    public class TestLocalBlazorForm : BaseTestClicks<LocalBlazorFormFixture> { public TestLocalBlazorForm( ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorFormFixture : BaseTestFixture { public LocalBlazorFormFixture() { AppExecutablePath = Utilities.BlazorWinFormsAppExe(); } }

    [Collection("TestLocalBlazorEmbeddedForm")]
    public class TestLocalBlazorEmbeddedForm : BaseTestClicks<LocalBlazorEmbeddedFormFixture> { public TestLocalBlazorEmbeddedForm(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorEmbeddedFormFixture : BaseTestFixture { public LocalBlazorEmbeddedFormFixture() { AppExecutablePath = Utilities.BlazorWinFormsEmbeddedAppExe(); } }

    //[Collection("TestLocalBlazorWebView")]
    //public class TestLocalBlazorWebView : BaseTestClicks<LocalBlazorWebViewFixture> { public TestLocalBlazorWebView(ITestOutputHelper output) : base(output) { } }
    //public class LocalBlazorWebViewFixture : BaseTestFixture { public LocalBlazorWebViewFixture() { AppExecutablePath = Utilities.BlazorWebViewAppExe(); } }

    [Collection("TestLocalBlazorWpf")]
    public class TestLocalBlazorWpf : BaseTestClicks<LocalBlazorWpfFixture> { public TestLocalBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorWpfFixture : BaseTestFixture { public LocalBlazorWpfFixture() { AppExecutablePath = Utilities.BlazorWpfAppExe(); } }
  
}
