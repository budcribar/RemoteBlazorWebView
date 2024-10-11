// BlazorTestHelper.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class BlazorTestHelper
    {
        private readonly IPage _page;
        private readonly ITestOutputHelper _output;

        public BlazorTestHelper(IPage page, ITestOutputHelper output)
        {
            _page = page;
            _output = output;
        }

        public async Task TestClicks(int numClicks)
        {
            try
            {
                // Navigate to the Counter component
                await _page.ClickAsync("text=Counter");
                _output.WriteLine("Clicked on the 'Counter' link.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error clicking 'Counter' link: {ex.Message}");
                throw;
            }

            // Wait for the Counter component to load
            await _page.WaitForSelectorAsync("h1:text('Counter')");

            // Locate the increment button and the paragraph displaying the count
            var incrementButton = _page.Locator("button:has-text('Click me')");
            var countParagraph = _page.Locator("p");

            // Ensure elements are visible
            await incrementButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await countParagraph.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            // Perform clicks without delay
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

            // Navigate back to the Home page
            try
            {
                await _page.ClickAsync("text=Home");
                _output.WriteLine("Navigated back to the 'Home' page.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error navigating back to 'Home' page: {ex.Message}");
                throw;
            }
        }
    }
}
