// TestLocalBlazorWebViewFixture.cs
namespace WebdriverTestProject
{
    public class TestLocalBlazorWebViewFixture : BaseTestFixture
    {
        protected override string AppExecutablePath => Utilities.BlazorWebViewAppExe();
    }
}
