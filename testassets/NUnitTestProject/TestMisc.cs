using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestMisc
    {
        [TestMethod]
        public void TestJavascriptCompiledForRelease()
        {
            FileInfo fi = new FileInfo(Utilities.JavascriptFile);
            var max = 400_000;
            Assert.IsTrue(fi.Length < max, $"{Path.GetFileName(Utilities.JavascriptFile)} >= {max}");

            Console.WriteLine($"{Path.GetFileName(Utilities.JavascriptFile)} is {fi.Length / 1024} kb long");
        }

    }
}
