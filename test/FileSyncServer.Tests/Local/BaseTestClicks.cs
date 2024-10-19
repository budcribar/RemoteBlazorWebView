// BaseTestClicks.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    /// <summary>
    /// Abstract base class containing shared test methods like TestClicks.
    /// Derived classes must supply the executable path via the fixture.
    /// </summary>
    public abstract class BaseTestClicks<T> : IAsyncLifetime where T : BaseTestFixture, new()
    {
        protected readonly T Fixture = new();
        protected readonly ITestOutputHelper Output;

        protected BaseTestClicks(ITestOutputHelper output)
        {
            Output = output;
        }

        // Initialize and Dispose methods delegate to the fixture
        public virtual async Task InitializeAsync()
        {
            await Fixture.InitializeAsync();
            // Additional shared initialization if needed
        }

        public virtual async Task DisposeAsync()
        {
            await Fixture.DisposeAsync();
            // Additional shared cleanup if needed
        }

        /// <summary>
        /// Shared TestClicks method that can be used across different test classes.
        /// </summary>
        /// <param name="numClicks">Number of clicks to perform.</param>
        [Theory]
        //[InlineData(10)]
        [InlineData(100)]
        public virtual async Task TestClicks(int numClicks)
        {
            Output.WriteLine($"Starting TestClicks with {numClicks} clicks.");

            try
            {
                // Navigate to the Counter component
                await Fixture.Page.ClickAsync("text=Counter");
                Output.WriteLine("Clicked on the 'Counter' link.");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error clicking 'Counter' link: {ex.Message}");
                throw;
            }

            // Wait for the Counter component to load
            await Fixture.Page.WaitForSelectorAsync("h1:text('Counter')");

            // Locate the increment button and the paragraph displaying the count
            var incrementButton = Fixture.Page.Locator("button:has-text('Click me')");
            var countParagraph = Fixture.Page.Locator("p");

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
            Output.WriteLine($"Clicked {numClicks} times in {sw.Elapsed.TotalSeconds} seconds.");

            // Get the count value
            string countText = await countParagraph.InnerTextAsync();
            Output.WriteLine($"Count displayed: {countText}");

            // Assert that the count contains the expected number
            Assert.Contains($"{numClicks}", countText);

            // Navigate back to the Home page
            try
            {
                await Fixture.Page.ClickAsync("text=Home");
                Output.WriteLine("Navigated back to the 'Home' page.");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error navigating back to 'Home' page: {ex.Message}");
                throw;
            }
        }

        // Additional shared test methods can be added here
    }
}
