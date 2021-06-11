using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System.Threading;
using System.Diagnostics;
using System;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorWpf : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWpfAppExe();
        }
    }
}