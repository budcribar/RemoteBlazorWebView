using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace FileSyncServer.Tests.Remote
{
    /// <summary>
    /// Sharing control, end to end: the relay, a real app (the tutorial's WPF or WinForms app) and real browsers.
    /// The test browser plays the owner: it opens the app's own page with ?role=owner, which is what an app window does in a
    /// shared session. The tutorial app's own window then becomes the first remote connection (it shows the read-only mirror
    /// when a browser connects), so it is the one control is shared with; a second browser on the mirror link is a viewer.
    /// </summary>
    public class SharedControlFixture : BaseTestRemoteFixture
    {
        public string OwnerQuery { get; set; } = "?role=owner";
        protected override string AppPath(string id) => $"app/{id}{OwnerQuery}";

        public IPage Owner => Pages[0];

        /// <summary>Another browser joins on the read-only link.</summary>
        public async Task<IPage> OpenMirror()
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });
            BrowserContexts.Add(context);
            var page = await context.NewPageAsync();
            await page.GotoAsync(Url + $"mirror/{Ids[0]}");
            return page;
        }

        public static ILocator Status(IPage page) => page.Locator(".rbwv-control-status");
        public static ILocator Give(IPage page) => page.Locator(".rbwv-control-give");
        public static ILocator Take(IPage page) => page.Locator(".rbwv-control-take");
        public static ILocator Count(IPage page) => page.Locator("p:has-text(\"Current count\")");

        public static async Task OpenCounter(IPage page)
        {
            await page.Locator("text=Counter").ClickAsync();
            await page.Locator("h1:text('Counter')").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        }

        /// <summary>The app's own window has joined as the first remote connection.</summary>
        public async Task WaitForTheAppWindowToJoin() =>
            await Expect(Owner.Locator(".rbwv-control-dot-on")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
    }

    public class SharedControlWpfFixture : SharedControlFixture { public SharedControlWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfApp; } }
    public class SharedControlWinFormsFixture : SharedControlFixture { public SharedControlWinFormsFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsApp; } }

    [Collection("TestSharedControlWpf")]
    public class TestSharedControlWpf(ITestOutputHelper output) : BaseTestSharedControl<SharedControlWpfFixture>(output) { }

    [Collection("TestSharedControlWinForms")]
    public class TestSharedControlWinForms(ITestOutputHelper output) : BaseTestSharedControl<SharedControlWinFormsFixture>(output) { }

    public abstract class BaseTestSharedControl<T>(ITestOutputHelper output) : IAsyncLifetime where T : SharedControlFixture, new()
    {
        private static readonly LocatorAssertionsToHaveTextOptions Soon = new() { Timeout = 20000 };
        protected readonly T _fixture = new();
        protected readonly ITestOutputHelper Output = output;

        public async Task InitializeAsync() => await _fixture.InitializeAsync();
        public async Task DisposeAsync() => await _fixture.DisposeAsync();

        [Fact]
        public async Task The_owner_starts_in_control_and_can_share_the_read_only_link()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;

            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", Soon);
            await Expect(owner.Locator(".rbwv-control-copy")).ToHaveTextAsync("Copy link", Soon);
            await Expect(SharedControlFixture.Take(owner)).ToHaveCountAsync(0);

            var state = await owner.EvaluateAsync<System.Text.Json.JsonElement>("() => { const s = window.RemoteControl.getState(); return { owner: s.isOwner, mine: s.iHaveControl, share: s.shareUrl }; }");
            Assert.True(state.GetProperty("owner").GetBoolean());
            Assert.True(state.GetProperty("mine").GetBoolean());
            Assert.EndsWith($"/mirror/{_fixture.Ids[0]}", state.GetProperty("share").GetString());

            // Once someone else is connected the dot lights up and control can be given.
            await _fixture.WaitForTheAppWindowToJoin();
            await Expect(owner.Locator(".rbwv-control-dot-on")).ToHaveAttributeAsync("title", "Someone is connected remotely");
            await Expect(SharedControlFixture.Give(owner)).ToHaveTextAsync("Give control", Soon);
        }

        [Fact]
        public async Task The_owner_gives_control_and_takes_it_back()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await _fixture.WaitForTheAppWindowToJoin();
            await SharedControlFixture.OpenCounter(owner);
            var button = owner.Locator(".btn");
            var count = SharedControlFixture.Count(owner);

            for (var i = 0; i < 3; i++) await button.ClickAsync();
            await Expect(count).ToContainTextAsync("Current count: 3", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

            await SharedControlFixture.Give(owner).ClickAsync();
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("Remote user has control", Soon);
            await Expect(SharedControlFixture.Take(owner)).ToHaveTextAsync("Take control", Soon);
            await Expect(SharedControlFixture.Give(owner)).ToHaveCountAsync(0);

            // Without control the owner's page is read-only: a click must not reach the app.
            await owner.EvaluateAsync("() => document.querySelector('.btn').click()");
            await Task.Delay(1500);
            await Expect(count).ToContainTextAsync("Current count: 3");

            await SharedControlFixture.Take(owner).ClickAsync();
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", Soon);
            await Expect(SharedControlFixture.Give(owner)).ToHaveTextAsync("Give control", Soon);
            await Expect(SharedControlFixture.Take(owner)).ToHaveCountAsync(0);

            await button.ClickAsync();
            await Expect(count).ToContainTextAsync("Current count: 4", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        }

        [Fact]
        public async Task Control_can_change_hands_many_times()
        {
            // Each hand-off restarts the message numbering on both sides and moves render acknowledgements to another
            // browser; the app must keep working through all of it.
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await _fixture.WaitForTheAppWindowToJoin();
            await SharedControlFixture.OpenCounter(owner);
            var button = owner.Locator(".btn");

            for (var round = 1; round <= 5; round++)
            {
                await button.ClickAsync();
                await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync($"Current count: {round}", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
                await SharedControlFixture.Give(owner).ClickAsync();
                await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("Remote user has control", Soon);
                await SharedControlFixture.Take(owner).ClickAsync();
                await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", Soon);
            }
        }

        [Fact]
        public async Task A_second_remote_connection_is_a_read_only_viewer()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await _fixture.WaitForTheAppWindowToJoin();
            await SharedControlFixture.OpenCounter(owner);
            await owner.Locator(".btn").ClickAsync();
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 2", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

            var viewer = await _fixture.OpenMirror();

            await Expect(SharedControlFixture.Status(viewer)).ToHaveTextAsync("View only", Soon);
            await Expect(viewer.Locator(".rbwv-control-btn")).ToHaveCountAsync(0); // no Give, Take or Copy link
            await Expect(SharedControlFixture.Count(viewer)).ToContainTextAsync("Current count: 2", new LocatorAssertionsToContainTextOptions { Timeout = 20000 });
            await Expect(owner.Locator(".rbwv-control-dot-on")).ToHaveAttributeAsync("title", "2 people are connected remotely", new LocatorAssertionsToHaveAttributeOptions { Timeout = 20000 });

            // What the owner does shows up for the viewer; what the viewer does goes nowhere.
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(viewer)).ToContainTextAsync("Current count: 3", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
            await viewer.EvaluateAsync("() => document.querySelector('.btn').click()");
            await Task.Delay(1500);
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 3");

            // Giving control goes to the first remote connection, never to the viewer.
            await SharedControlFixture.Give(owner).ClickAsync();
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("Remote user has control", Soon);
            await Expect(SharedControlFixture.Status(viewer)).ToHaveTextAsync("View only", Soon);
            await Expect(viewer.Locator(".rbwv-control-btn")).ToHaveCountAsync(0);
        }

        [Fact]
        public async Task A_viewer_leaving_does_not_disturb_the_session()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await _fixture.WaitForTheAppWindowToJoin();
            await SharedControlFixture.OpenCounter(owner);

            var viewer = await _fixture.OpenMirror();
            await Expect(owner.Locator(".rbwv-control-dot-on")).ToHaveAttributeAsync("title", "2 people are connected remotely", new LocatorAssertionsToHaveAttributeOptions { Timeout = 20000 });
            await viewer.CloseAsync();

            await Expect(owner.Locator(".rbwv-control-dot-on")).ToHaveAttributeAsync("title", "Someone is connected remotely", new LocatorAssertionsToHaveAttributeOptions { Timeout = 20000 });
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", Soon);
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        }

        [Fact]
        public async Task An_unattended_session_gives_control_as_soon_as_someone_joins_and_only_once()
        {
            _fixture.OwnerQuery = "?role=owner&control=remote";
            await _fixture.Startup(1);
            var owner = _fixture.Owner;

            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("Remote user has control", new LocatorAssertionsToHaveTextOptions { Timeout = 30000 });

            await SharedControlFixture.Take(owner).ClickAsync();
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", Soon);
            await Task.Delay(3000); // it must stay with the owner: the automatic hand-over happens once per remote connection
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control");
        }

        [Fact]
        public async Task The_restart_button_restarts_the_app_and_the_page_comes_back_by_itself()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await SharedControlFixture.OpenCounter(owner);
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

            // The button is opt-in: an app asks for it on the bar's placeholder. The tutorial app has no placeholder, so add one,
            // pinned to the top of the window: the bar sits exactly over its placeholder, and one at the end of the page would
            // put the bar below what the browser shows.
            await Expect(owner.Locator(".rbwv-control-restart")).ToHaveCountAsync(0);
            await owner.EvaluateAsync("() => document.body.insertAdjacentHTML('beforeend', '<div id=\"rbwv-session-controls\" data-restart=\"true\" style=\"position:fixed;top:4px;right:8px\"></div>')");

            await owner.Locator(".rbwv-control-restart").ClickAsync();
            await Expect(owner.Locator(".rbwv-control-question")).ToHaveTextAsync("Restart the app?", Soon);
            await owner.Locator(".rbwv-control-cancel").ClickAsync(); // Cancel changes nothing
            await Expect(owner.Locator(".rbwv-control-question")).ToHaveCountAsync(0);
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1");

            await owner.Locator(".rbwv-control-restart").ClickAsync();
            await owner.Locator(".rbwv-control-confirm").ClickAsync();

            // The app restarts on the same session and this page loads again without anyone touching it: a new copy, so
            // the counter is back at zero and the owner is in control.
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", new LocatorAssertionsToHaveTextOptions { Timeout = 120000 });
            await SharedControlFixture.OpenCounter(owner);
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 0", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        }

        [Fact]
        public async Task An_app_page_without_an_owner_shows_no_session_bar()
        {
            // The classic setup (a browser drives the app, nobody is the owner, no voice) must look as it did before sharing control existed.
            _fixture.OwnerQuery = "";
            await _fixture.Startup(1);
            var page = _fixture.Owner;
            await SharedControlFixture.OpenCounter(page);
            await Task.Delay(3000); // long enough for the app's window to join as a mirror

            await Expect(page.Locator(".rbwv-control-bar")).ToBeHiddenAsync();
            await page.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(page)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        }
    }
}
