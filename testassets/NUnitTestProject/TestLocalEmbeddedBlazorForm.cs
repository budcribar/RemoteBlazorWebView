using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalEmbeddedBlazorForm : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWinFormsEmbeddedAppExe();
        }
       
    }
}