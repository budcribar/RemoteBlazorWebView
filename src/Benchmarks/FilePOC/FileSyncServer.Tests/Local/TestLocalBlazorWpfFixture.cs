// TestLocalBlazorFormFixture.cs


namespace WebdriverTestProject
{
    public class TestLocalBlazorWpfFixture : BaseTestFixture
    {
        protected override string AppExecutablePath => Utilities.BlazorWpfAppExe();
    }
}
