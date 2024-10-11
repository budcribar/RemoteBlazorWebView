
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    [Collection("TestLocal")]
    public class TestLocalBlazorForm : BaseTestClicks<LocalBlazorFormFixture> { public TestLocalBlazorForm( ITestOutputHelper output) : base( output) { } }
    public class LocalBlazorFormFixture : BaseTestFixture { public LocalBlazorFormFixture() : base(Utilities.BlazorWinFormsAppExe()) { } }
    [CollectionDefinition("TestLocal", DisableParallelization = true)]
    public class LocalBlazorFormFixtureCollection : ICollectionFixture<LocalBlazorFormFixture> { }


    [Collection("TestLocal")]
    public class TestLocalBlazorEmbeddedForm : BaseTestClicks<LocalBlazorEmbeddedFormFixture> { public TestLocalBlazorEmbeddedForm(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorEmbeddedFormFixture : BaseTestFixture { public LocalBlazorEmbeddedFormFixture() : base(Utilities.BlazorWinFormsEmbeddedAppExe()) { } }
    [CollectionDefinition("TestLocal", DisableParallelization = true)]
    public class LocalBlazorEmbeddedFormFixtureCollection : ICollectionFixture<LocalBlazorEmbeddedFormFixture> { }


    [Collection("TestLocal")]
    public class TestLocalBlazorWebView : BaseTestClicks<LocalBlazorWebViewFixture> { public TestLocalBlazorWebView(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorWebViewFixture : BaseTestFixture { public LocalBlazorWebViewFixture() : base(Utilities.BlazorWebViewAppExe()) { } }
    [CollectionDefinition("TestLocal", DisableParallelization = true)]
    public class LocalBlazorWebViewFixtureCollection : ICollectionFixture<LocalBlazorWebViewFixture> { }


    [Collection("TestLocal")]
    public class TestLocalBlazorWpf : BaseTestClicks<LocalBlazorWpfFixture> { public TestLocalBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class LocalBlazorWpfFixture : BaseTestFixture { public LocalBlazorWpfFixture() : base(Utilities.BlazorWpfAppExe()) { } }
    [CollectionDefinition("TestLocal", DisableParallelization = true)]
    public class LocalBlazorWpfFixtureCollection : ICollectionFixture<LocalBlazorWpfFixture> { }

}
