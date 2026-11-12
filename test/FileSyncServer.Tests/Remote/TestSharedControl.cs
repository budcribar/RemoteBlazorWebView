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
        public async Task<IPage> OpenMirror(string? baseUrl = null)
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });
            BrowserContexts.Add(context);
            var page = await context.NewPageAsync();
            await page.GotoAsync((baseUrl ?? Url) + $"mirror/{Ids[0]}");
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
    [Trait("Category", "Slow")]
    public class TestSharedControlWpf(ITestOutputHelper output) : BaseTestSharedControl<SharedControlWpfFixture>(output) { }

    [Collection("TestSharedControlWinForms")]
    [Trait("Category", "Slow")]
    public class TestSharedControlWinForms(ITestOutputHelper output) : BaseTestSharedControl<SharedControlWinFormsFixture>(output) { }

    [Trait("Category", "Slow")]
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
        public async Task A_viewer_that_loses_its_connection_says_so_and_comes_back_by_itself()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await _fixture.WaitForTheAppWindowToJoin();
            await SharedControlFixture.OpenCounter(owner);
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

            // The viewer reaches the relay through a port of the test's own, which the test can cut and restore.
            using var network = new TcpForwarder(new System.Uri(_fixture.Url).Port);
            var viewer = await _fixture.OpenMirror($"https://localhost:{network.Port}/");
            await Expect(SharedControlFixture.Count(viewer)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 20000 });
            await Expect(viewer.Locator("#rbwv-connection-lost")).ToHaveCountAsync(0);

            // The viewer's network goes away: its page says so instead of standing there looking alive.
            network.Disconnect();
            await Expect(viewer.Locator("#rbwv-connection-lost")).ToContainTextAsync("Connection lost", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });

            // The session carries on without it.
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 2", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

            // The network returns: the page loads again by itself and shows the session as it is now.
            network.Connect();
            await Expect(SharedControlFixture.Count(viewer)).ToContainTextAsync("Current count: 2", new LocatorAssertionsToContainTextOptions { Timeout = 45000 });
            await Expect(viewer.Locator("#rbwv-connection-lost")).ToHaveCountAsync(0);
            await Expect(SharedControlFixture.Status(viewer)).ToHaveTextAsync("View only", Soon);

            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(viewer)).ToContainTextAsync("Current count: 3", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
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

            // The button is opt-in: an app asks for it on the bar's placeholder. The tutorial app's placeholder (in its layout)
            // does not, so ask here.
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", Soon);
            await Expect(owner.Locator(".rbwv-control-restart")).ToHaveCountAsync(0);
            await owner.EvaluateAsync("() => document.getElementById('rbwv-session-controls').setAttribute('data-restart', 'true')");

            await owner.Locator(".rbwv-control-restart").ClickAsync();
            await Expect(owner.Locator(".rbwv-control-question")).ToHaveTextAsync("Restart the app?", Soon);
            await owner.Locator(".rbwv-control-cancel").ClickAsync(); // Cancel changes nothing
            await Expect(owner.Locator(".rbwv-control-question")).ToHaveCountAsync(0);
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1");

            await owner.Locator(".rbwv-control-restart").ClickAsync();
            await owner.Locator(".rbwv-control-confirm").ClickAsync();
            // Wait for the restart to be under way first: until the relay announces it the bar still says "You have control",
            // which would be mistaken for the page having come back.
            await Expect(SharedControlFixture.Status(owner)).ToContainTextAsync("Restarting", new LocatorAssertionsToContainTextOptions { Timeout = 20000 });

            // The app restarts on the same session and this page loads again without anyone touching it: a new copy, so
            // the counter is back at zero and the owner is in control.
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control", new LocatorAssertionsToHaveTextOptions { Timeout = 120000 });
            await SharedControlFixture.OpenCounter(owner);
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 0", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
            await owner.Locator(".btn").ClickAsync();
            await Expect(SharedControlFixture.Count(owner)).ToContainTextAsync("Current count: 1", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        }

        [Fact]
        public async Task Without_the_placeholder_there_is_no_session_bar()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            var bar = owner.Locator(".rbwv-control-bar");
            // The tutorial app's layout has the placeholder, so the bar is there.
            await Expect(bar).ToBeVisibleAsync(new() { Timeout = 20000 });
            await Expect(bar).ToContainClassAsync("rbwv-control-slotted");

            // The bar is opt-in: an app without the placeholder gets nothing drawn on its pages, even when it shares control.
            // (Taken away here by renaming it; a resize makes the bar look for its placeholder again.)
            await owner.EvaluateAsync("() => { document.getElementById('rbwv-session-controls').id = 'not-the-placeholder'; window.dispatchEvent(new Event('resize')); }");
            await Expect(bar).ToBeHiddenAsync(new() { Timeout = 20000 });
            // The state is still there for an app that draws its own.
            Assert.True(await owner.EvaluateAsync<bool>("() => window.RemoteControl.getState().iHaveControl"));

            await owner.EvaluateAsync("() => { document.getElementById('not-the-placeholder').id = 'rbwv-session-controls'; window.dispatchEvent(new Event('resize')); }");
            await Expect(bar).ToBeVisibleAsync(new() { Timeout = 20000 });
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You have control");
        }

        [Fact]
        public async Task A_template_in_the_placeholder_is_the_apps_own_bar()
        {
            await _fixture.Startup(1);
            var owner = _fixture.Owner;
            await _fixture.WaitForTheAppWindowToJoin();
            await SharedControlFixture.OpenCounter(owner);

            // The app's own markup: each browser gets a copy with only the parts that apply to it, the status filled in and
            // the buttons wired. Wording, classes and the elements that are not parts stay the app's.
            await owner.EvaluateAsync(@"() => document.getElementById('rbwv-session-controls').innerHTML =
                '<template><em class=""mine"">Session</em> <b data-rbwv=""status"" data-text-you=""You are driving""></b> ' +
                '<button class=""mine-btn"" data-rbwv=""give"">Hand over</button> <button class=""mine-btn"" data-rbwv=""take"">Take back</button></template>'");

            var bar = owner.Locator(".rbwv-control-bar");
            await Expect(bar).ToContainClassAsync("rbwv-control-custom", new() { Timeout = 20000 });
            await Expect(bar.Locator("em.mine")).ToHaveTextAsync("Session");
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You are driving", Soon);
            await Expect(SharedControlFixture.Give(owner)).ToHaveTextAsync("Hand over", Soon);
            await Expect(SharedControlFixture.Take(owner)).ToHaveCountAsync(0);
            await Expect(owner.Locator(".rbwv-control-copy")).ToHaveCountAsync(0); // not in this template

            await SharedControlFixture.Give(owner).ClickAsync();
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("Remote user has control", Soon);
            await Expect(SharedControlFixture.Take(owner)).ToHaveTextAsync("Take back", Soon);
            await Expect(SharedControlFixture.Give(owner)).ToHaveCountAsync(0);

            await SharedControlFixture.Take(owner).ClickAsync();
            await Expect(SharedControlFixture.Status(owner)).ToHaveTextAsync("You are driving", Soon);
            await owner.Locator(".btn").ClickAsync(); // the owner is in control of the app again
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
