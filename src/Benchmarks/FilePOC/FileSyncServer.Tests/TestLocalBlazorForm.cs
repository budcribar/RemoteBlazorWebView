// TestLocalBlazorForm.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class TestLocalBlazorForm : IClassFixture<TestBlazorFormFixture>
    {
        private readonly TestBlazorFormFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TestLocalBlazorForm(TestBlazorFormFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        public async Task TestClicks(int numClicks)
        {
            var page = _fixture.Page;

            try
            {
                // Example: Click on a link or button labeled "Counter"
                await page.ClickAsync("text=Counter");
                _output.WriteLine("Clicked on the 'Counter' link.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error clicking 'Counter' link: {ex.Message}");
                throw;
            }

            // Wait for the Counter component to load
            await page.WaitForSelectorAsync("h1:text('Counter')");

            // Locate the increment button and the paragraph displaying the count
            var incrementButton = page.Locator("button:has-text('Click me')");
            var countParagraph = page.Locator("p");

            // Ensure elements are visible
            await incrementButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await countParagraph.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // Perform clicks
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < numClicks; i++)
            {
                await incrementButton.ClickAsync();
            }
            sw.Stop();
            _output.WriteLine($"Clicked {numClicks} times in {sw.Elapsed.TotalSeconds} seconds.");

            // Get the count value
            string countText = await countParagraph.InnerTextAsync();
            _output.WriteLine($"Count displayed: {countText}");

            // Assert that the count contains the expected number
            Assert.Contains($"{numClicks}", countText);

            await page.ClickAsync("text=Home");
        }  
    }
}
