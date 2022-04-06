using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorWebViewDebug : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWebViewAppExe();
        }
    }
}