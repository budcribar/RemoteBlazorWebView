// TestLocalBlazorFormFixture.cs


namespace WebdriverTestProject
{
    public class TestLocalBlazorEmbeddedFormFixture : BaseTestFixture
    {
        protected override string AppExecutablePath => Utilities.BlazorWinFormsEmbeddedAppExe();
    }
}
