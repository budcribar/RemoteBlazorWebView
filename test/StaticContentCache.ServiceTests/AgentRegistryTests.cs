using System.Security.Claims;
using System.Threading.Channels;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Agents;

namespace StaticContentCache.Tests;

public class AgentRegistryTests
{
    private const string ServerUrl = "https://server.remoteblazorwebview.com:34544";
    private static readonly string[] ExpectedClosedKeys = ["alpha", "beta"];

    // ---------------------------------------------------------------- consent rule

    // Every combination of (unattended, ceiling, admin key configured, admin key valid).
    // A "valid" key without one configured can't happen in practice; it must still never loosen anything.
    [Theory]
    // No admin key configured: always allowed, always asks.
    [InlineData(false, ConsentMode.ConsentAlwaysAsk, false, false, true, true)]
    [InlineData(true, ConsentMode.ConsentAlwaysAsk, false, false, true, true)]
    [InlineData(false, ConsentMode.ConsentUnattendedAllowed, false, false, true, true)]
    [InlineData(true, ConsentMode.ConsentUnattendedAllowed, false, false, true, true)]
    [InlineData(false, ConsentMode.ConsentAlwaysAsk, false, true, true, true)]
    [InlineData(true, ConsentMode.ConsentAlwaysAsk, false, true, true, true)]
    [InlineData(false, ConsentMode.ConsentUnattendedAllowed, false, true, true, true)]
    [InlineData(true, ConsentMode.ConsentUnattendedAllowed, false, true, true, true)]
    // Admin key configured but missing or wrong: refused.
    [InlineData(false, ConsentMode.ConsentAlwaysAsk, true, false, false, true)]
    [InlineData(true, ConsentMode.ConsentAlwaysAsk, true, false, false, true)]
    [InlineData(false, ConsentMode.ConsentUnattendedAllowed, true, false, false, true)]
    [InlineData(true, ConsentMode.ConsentUnattendedAllowed, true, false, false, true)]
    // Admin key valid: skip the prompt only when unattended was asked for and the ceiling allows it.
    [InlineData(false, ConsentMode.ConsentAlwaysAsk, true, true, true, true)]
    [InlineData(true, ConsentMode.ConsentAlwaysAsk, true, true, true, true)]
    [InlineData(false, ConsentMode.ConsentUnattendedAllowed, true, true, true, true)]
    [InlineData(true, ConsentMode.ConsentUnattendedAllowed, true, true, true, false)]
    public void Consent_policy_covers_every_combination(bool unattended, ConsentMode ceiling, bool keyConfigured, bool keyValid, bool allowed, bool requireConsent)
    {
        var decision = ConsentPolicy.Decide(unattended, ceiling, keyConfigured, keyValid);
        Assert.Equal(allowed, decision.Allowed);
        Assert.Equal(requireConsent, decision.RequireConsent);
        Assert.Equal(allowed ? null : ConsentPolicy.AdminKeyRequired, decision.Error);
    }

    [Fact]
    public void Admin_key_is_read_from_options_and_compared_exactly()
    {
        Assert.False(AgentOptions.Create(null, null).AdminKeyConfigured);
        Assert.False(AgentOptions.Create(null, null, "  ").AdminKeyConfigured);
        Assert.False(AgentOptions.Create(null, null).IsAdminKeyValid("anything"));

        var options = AgentOptions.Create(null, null, " s3cret ");
        Assert.True(options.AdminKeyConfigured);
        Assert.True(options.IsAdminKeyValid("s3cret"));
        Assert.False(options.IsAdminKeyValid("s3cret "));
        Assert.False(options.IsAdminKeyValid("S3CRET"));
        Assert.False(options.IsAdminKeyValid(""));
        Assert.False(options.IsAdminKeyValid(null));

        var context = new CallContext(CancellationToken.None);
        Assert.Null(AgentAdminService.GetAdminKey(context));
        context.RequestHeaders.Add("x-admin-key", "s3cret");
        Assert.Equal("s3cret", AgentAdminService.GetAdminKey(context));
    }

    [Fact]
    public void Boot_time_from_the_hello_is_listed_and_a_reconnect_after_reboot_replaces_it()
    {
        var registry = new AgentRegistry(new ManualTime());
        var firstBoot = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));
        var hello = Hello("m-1");
        hello.BootTime = firstBoot;
        registry.Register(hello, "10.0.0.1", null);
        Assert.Equal(firstBoot, registry.GetAgent("m-1")!.BootTime);

        var rebooted = Hello("m-1");
        rebooted.BootTime = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 11, 19, 17, 0, 0, TimeSpan.Zero));
        registry.Register(rebooted, "10.0.0.1", null);
        Assert.Equal(rebooted.BootTime, registry.GetAgent("m-1")!.BootTime);

        // An older agent sends none: nothing is made up.
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.Null(registry.GetAgent("m-1")!.BootTime);
    }

    [Fact]
    public void StartSession_with_a_wrong_admin_key_is_denied_before_anything_else()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1", ceiling: ConsentMode.ConsentUnattendedAllowed), "10.0.0.1", null);

        var denied = registry.StartSession("m-1", true, "Bob", ServerUrl, adminKeyConfigured: true, adminKeyValid: false);
        Assert.True(denied.Denied);
        Assert.Equal(ConsentPolicy.AdminKeyRequired, denied.Response.Error);
        Assert.True(registry.StartSession("nope", true, "Bob", ServerUrl, true, false).Denied);
        Assert.False(connection.Commands.TryRead(out _));

        // No admin key configured: unattended is impossible.
        var asked = registry.StartSession("m-1", true, "Bob", ServerUrl, adminKeyConfigured: false, adminKeyValid: false);
        Assert.True(asked.Response.Accepted);
        Assert.True(connection.Commands.TryRead(out var command));
        Assert.True(command!.Launch.RequireConsent);
    }

    [Fact]
    public void PublishRelease_broadcasts_to_online_agents_and_replays_to_newly_connected_agents()
    {
        var registry = new AgentRegistry(new ManualTime());
        var online = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var offline = registry.Register(Hello("m-2"), "10.0.0.2", null);
        registry.Disconnect(offline);

        int notified = registry.PublishRelease(new ReleaseAvailableCommand
        {
            StableVersion = "1.3.0",
            PrereleaseVersion = "1.3.0-ci.800.1",
        });

        Assert.Equal(1, notified);
        Assert.True(online.Commands.TryRead(out var pushed));
        Assert.Equal(HubCommand.BodyOneofCase.ReleaseAvailable, pushed!.BodyCase);
        Assert.Equal("1.3.0", pushed.ReleaseAvailable.StableVersion);
        Assert.Equal("1.3.0-ci.800.1", pushed.ReleaseAvailable.PrereleaseVersion);
        Assert.False(offline.Commands.TryRead(out _));

        // A newly connecting agent receives the cached release announcement immediately on register.
        var lateJoiner = registry.Register(Hello("m-3"), "10.0.0.3", null);
        Assert.True(lateJoiner.Commands.TryRead(out var replayed));
        Assert.Equal(HubCommand.BodyOneofCase.ReleaseAvailable, replayed!.BodyCase);
        Assert.Equal("1.3.0", replayed.ReleaseAvailable.StableVersion);
        Assert.Equal("1.3.0-ci.800.1", replayed.ReleaseAvailable.PrereleaseVersion);
    }

    // ---------------------------------------------------------------- enrollment keys and options

    [Fact]
    public void Enrollment_keys_are_comma_separated_and_any_key_is_accepted_when_unset()
    {
        var open = AgentOptions.Create(null, null);
        Assert.True(open.AcceptsAnyKey);
        Assert.True(open.IsKeyAccepted("whatever"));
        Assert.True(AgentOptions.Create(" , ", null).AcceptsAnyKey);

        var closed = AgentOptions.Create(" alpha , beta,,", "https://relay.example:34544/");
        Assert.Equal(ExpectedClosedKeys, closed.EnrollmentKeys);
        Assert.True(closed.IsKeyAccepted("beta"));
        Assert.False(closed.IsKeyAccepted("gamma"));
        Assert.False(closed.IsKeyAccepted(""));
        Assert.Equal("https://relay.example:34544", closed.PublicBaseUrl);
    }

    [Fact]
    public void With_keys_required_and_none_set_every_key_is_refused()
    {
        var locked = AgentOptions.Create(null, null, requireKeys: true);
        Assert.False(locked.AcceptsAnyKey);
        Assert.True(locked.RefusesEveryKey);
        Assert.False(locked.IsKeyAccepted("whatever"));
        Assert.False(locked.IsKeyAccepted(""));
        Assert.False(locked.IsKeyAccepted(null));

        var decision = new PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy().CheckEnrollment("whatever", locked);
        Assert.False(decision.Accepted);
        Assert.Equal(PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy.NoKeysConfiguredReason, decision.DenialReason);

        var listed = AgentOptions.Create("alpha", null, requireKeys: true);
        Assert.False(listed.RefusesEveryKey);
        Assert.True(listed.IsKeyAccepted("alpha"));
        Assert.False(listed.IsKeyAccepted("beta"));
        Assert.Equal("The enrollment key was not accepted",
            new PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy().CheckEnrollment("beta", listed).DenialReason);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("development", false)]
    public void Enrollment_keys_are_required_outside_Development(string? environment, bool required)
    {
        var settings = new Dictionary<string, string?> { ["environment"] = environment };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var options = AgentOptions.FromConfiguration(configuration);
        Assert.Equal(required, options.RequireKeys);
        Assert.Equal(!required, options.IsKeyAccepted("whatever"));

        settings[AgentOptions.EnrollmentKeysVariable] = "alpha";
        var keyed = AgentOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        Assert.True(keyed.IsKeyAccepted("alpha"));
        Assert.False(keyed.IsKeyAccepted("whatever"));
    }

    [Fact]
    public void Server_url_uses_PUBLIC_BASE_URL_else_the_request_scheme_and_host()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("server.remoteblazorwebview.com:34544");
        var context = new CallContext(CancellationToken.None, http);

        Assert.Equal(ServerUrl, AgentAdminService.GetServerUrl(AgentOptions.Create(null, null), context));
        Assert.Equal("https://other.example", AgentAdminService.GetServerUrl(AgentOptions.Create(null, "https://other.example/"), context));
    }

    [Fact]
    public void Requester_is_the_signed_in_user_else_the_request_else_a_default()
    {
        var anonymous = new CallContext(CancellationToken.None, new DefaultHttpContext());
        Assert.Equal("Bob", AgentAdminService.GetRequester("Bob", anonymous));
        Assert.Equal(AgentAdminService.DefaultRequester, AgentAdminService.GetRequester(null, anonymous));

        var signedIn = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("name", "Alice Admin") }, "test")) };
        Assert.Equal("Alice Admin", AgentAdminService.GetRequester("Bob", new CallContext(CancellationToken.None, signedIn)));
        Assert.Equal("Alice Admin", AgentAdminService.GetRequester(null, new CallContext(CancellationToken.None, signedIn)));
        Assert.Equal("Alice Admin (AI connector)",
            AgentAdminService.GetRequester("Alice Admin (AI connector)", new CallContext(CancellationToken.None, signedIn)));
    }

    [Theory]
    [InlineData("Alice Admin (AI connector)", "Alice Admin (AI connector)")]
    [InlineData("  Alice Admin (AI connector)  ", "Alice Admin (AI connector)")]
    [InlineData("Alice Admin (web-2)", "Alice Admin (web-2)")]
    [InlineData("Bob (AI connector)", "Alice Admin")]              // another name
    [InlineData("Alice Admin and Bob (AI connector)", "Alice Admin")]
    [InlineData("Alice Admin (Bob) (AI connector)", "Alice Admin")] // parentheses inside the label
    [InlineData("Alice Admin (really Bob, the owner)", "Alice Admin")]
    [InlineData("Alice Admin (0123456789012345678901234567890123)", "Alice Admin")] // 34 characters
    [InlineData("Alice Admin ()", "Alice Admin")]
    [InlineData("Alice Admin (AI connector", "Alice Admin")]
    [InlineData("Alice Admin", "Alice Admin")]
    [InlineData("", "Alice Admin")]
    [InlineData(null, "Alice Admin")]
    public void A_signed_in_requester_keeps_only_a_short_label_after_their_own_name(string? requested, string expected) =>
        Assert.Equal(expected, AgentAdminService.WithLabel("Alice Admin", requested));

    // ---------------------------------------------------------------- online / offline / expiry

    [Fact]
    public void Agent_goes_offline_90_s_after_its_last_message_and_is_forgotten_after_7_days()
    {
        var time = new ManualTime();
        var registry = new AgentRegistry(time);
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.True(connection.IsNew);

        var agent = registry.GetAgent("m-1")!;
        Assert.True(agent.Online);
        Assert.Equal("PC-m-1", agent.MachineName);
        Assert.Equal("10.0.0.1", agent.IpAddress);

        time.Advance(TimeSpan.FromSeconds(60));
        registry.OnHeartbeat(connection, new Heartbeat { SignedInUser = @"CONTOSO\pat" });
        time.Advance(TimeSpan.FromSeconds(89));
        Assert.Empty(registry.Sweep().WentOffline);
        Assert.True(registry.GetAgent("m-1")!.Online);
        Assert.Equal(@"CONTOSO\pat", registry.GetAgent("m-1")!.SignedInUser);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(registry.Sweep().WentOffline);
        Assert.False(registry.GetAgent("m-1")!.Online);
        Assert.Single(registry.GetAgentList().Agents);

        // A late heartbeat on the same stream brings it back.
        registry.OnHeartbeat(connection, new Heartbeat());
        Assert.True(registry.GetAgent("m-1")!.Online);

        // Stream ends: offline straight away, but still listed.
        Assert.True(registry.Disconnect(connection));
        Assert.False(registry.GetAgent("m-1")!.Online);
        time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Assert.Empty(registry.Sweep().Forgotten);
        Assert.NotNull(registry.GetAgent("m-1"));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(registry.Sweep().Forgotten);
        Assert.Null(registry.GetAgent("m-1"));
        Assert.Empty(registry.GetAgentList().Agents);
    }

    [Fact]
    public void A_new_stream_for_the_same_machine_replaces_the_old_one()
    {
        var registry = new AgentRegistry(new ManualTime());
        var first = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var second = registry.Register(Hello("m-1"), "10.0.0.2", null);

        Assert.True(first.Replaced.IsCancellationRequested);
        Assert.False(second.Replaced.IsCancellationRequested);
        Assert.True(second.ReplacedExisting);
        Assert.False(second.IsNew);

        // The old stream ending must not take the agent offline.
        Assert.False(registry.Disconnect(first));
        var agent = registry.GetAgent("m-1")!;
        Assert.True(agent.Online);
        Assert.Equal("10.0.0.2", agent.IpAddress);
    }

    [Fact]
    public async Task Changes_complete_the_change_signal()
    {
        var registry = new AgentRegistry(new ManualTime());
        var signal = registry.Changed;
        Assert.False(signal.IsCompleted);
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        await signal.WaitAsync(TimeSpan.FromSeconds(2));
    }

    // ---------------------------------------------------------------- StartSession

    [Fact]
    public void StartSession_refuses_an_offline_unknown_or_busy_agent()
    {
        var registry = new AgentRegistry(new ManualTime());
        Assert.Equal(AgentRegistry.UnknownAgentError, registry.StartSession("nope", false, "Bob", ServerUrl, true, true).Response.Error);
        Assert.Equal(AgentRegistry.NoAgentError, registry.StartSession("", false, "Bob", ServerUrl, true, true).Response.Error);

        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        registry.Disconnect(connection);
        var offline = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.False(offline.Accepted);
        Assert.Equal(AgentRegistry.OfflineError, offline.Error);
        Assert.True(string.IsNullOrEmpty(offline.RequestId));

        connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        registry.OnHeartbeat(connection, new Heartbeat { SessionRunning = true, SessionId = Guid.NewGuid().ToString() });
        var busy = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.False(busy.Accepted);
        Assert.Equal(AgentRegistry.BusyError, busy.Error);
        Assert.False(connection.Commands.TryRead(out _));
    }

    [Fact]
    public void StartSession_refuses_an_agent_that_went_silent()
    {
        var time = new ManualTime();
        var registry = new AgentRegistry(time);
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        time.Advance(AgentRegistry.OfflineAfter);
        registry.Sweep();
        Assert.Equal(AgentRegistry.OfflineError, registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response.Error);
    }

    [Theory]
    [InlineData(ConsentMode.ConsentAlwaysAsk, true, true)]
    [InlineData(ConsentMode.ConsentUnattendedAllowed, true, false)]
    [InlineData(ConsentMode.ConsentUnattendedAllowed, false, true)]
    public void StartSession_queues_a_launch_command_and_records_it_as_queued(ConsentMode ceiling, bool unattended, bool expectConsent)
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1", ceiling: ceiling), "10.0.0.1", null);

        var response = registry.StartSession("m-1", unattended, "Bob", ServerUrl + "/", true, true).Response;

        Assert.True(response.Accepted, response.Error);
        Assert.True(Guid.TryParse(response.SessionId, out _));
        Assert.True(Guid.TryParse(response.RequestId, out _));
        Assert.Equal($"{ServerUrl}/mirror/{response.SessionId}", response.AppUrl); // the link the requester joins at

        Assert.True(connection.Commands.TryRead(out var command));
        Assert.Equal(HubCommand.BodyOneofCase.Launch, command!.BodyCase);
        Assert.Equal(response.RequestId, command.Launch.RequestId);
        Assert.Equal(response.SessionId, command.Launch.SessionId);
        Assert.Equal(ServerUrl, command.Launch.ServerUrl);
        Assert.Equal("Bob", command.Launch.Requester);
        Assert.Equal(expectConsent, command.Launch.RequireConsent);

        var latest = registry.GetLatestProgress(response.RequestId)!;
        Assert.Equal(LaunchStage.StageQueued, latest.Stage);
        Assert.Equal(response.SessionId, latest.SessionId);
    }

    [Fact]
    public void CancelLaunch_queues_a_cancel_command()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var response = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.True(connection.Commands.TryRead(out _));

        Assert.Equal(CancelLaunchOutcome.Queued, registry.CancelLaunch(response.RequestId));
        Assert.True(connection.Commands.TryRead(out var cancel));
        Assert.Equal(response.RequestId, cancel!.Cancel.RequestId);
        Assert.Equal(CancelLaunchOutcome.NotFound, registry.CancelLaunch(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void EndSession_cancels_the_running_launch_and_reports_what_it_cannot_end()
    {
        var registry = new AgentRegistry(new ManualTime());
        Assert.Equal(EndSessionOutcome.EndSessionNotFound, registry.EndSession("m-1").Outcome);
        Assert.Equal(EndSessionOutcome.EndSessionNotFound, registry.EndSession("").Outcome);

        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.Equal(EndSessionOutcome.EndSessionNotRunning, registry.EndSession("m-1").Outcome);

        // A session someone started at the PC: running, but the relay has no launch to cancel.
        registry.OnHeartbeat(connection, new Heartbeat { SessionRunning = true, SessionId = Guid.NewGuid().ToString() });
        Assert.Equal(EndSessionOutcome.EndSessionNotAgentLaunched, registry.EndSession("m-1").Outcome);
        registry.OnHeartbeat(connection, new Heartbeat());
        Assert.False(connection.Commands.TryRead(out _));

        var started = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.True(connection.Commands.TryRead(out _));
        registry.OnProgress(connection, new LaunchProgress { RequestId = started.RequestId, Stage = LaunchStage.StageReady, SessionId = started.SessionId });

        var ended = registry.EndSession("M-1");
        Assert.Equal(EndSessionOutcome.EndSessionRequested, ended.Outcome);
        Assert.Equal(started.RequestId, ended.RequestId);
        Assert.True(connection.Commands.TryRead(out var cancel));
        Assert.Equal(started.RequestId, cancel!.Cancel.RequestId);

        // The agent closes the app and reports ENDED; after that there is nothing left to end.
        registry.OnProgress(connection, new LaunchProgress { RequestId = started.RequestId, Stage = LaunchStage.StageEnded, SessionId = started.SessionId });
        Assert.Equal(EndSessionOutcome.EndSessionNotRunning, registry.EndSession("m-1").Outcome);
        Assert.False(connection.Commands.TryRead(out _));
    }

    [Fact]
    public void EndSession_also_stops_a_start_in_progress_and_respects_group_scope_and_offline()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, "shop-a");
        var started = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true, "shop-a").Response;
        Assert.True(started.Accepted);
        Assert.True(connection.Commands.TryRead(out _));

        Assert.Equal(EndSessionOutcome.EndSessionNotFound, registry.EndSession("m-1", "shop-b").Outcome);
        Assert.False(connection.Commands.TryRead(out _));

        var ended = registry.EndSession("m-1", "shop-a");
        Assert.Equal(EndSessionOutcome.EndSessionRequested, ended.Outcome);
        Assert.True(connection.Commands.TryRead(out var cancel));
        Assert.Equal(started.RequestId, cancel!.Cancel.RequestId);

        // A running app outlives the agent's connection; while the computer is offline the command cannot reach it.
        registry.OnProgress(connection, new LaunchProgress { RequestId = started.RequestId, Stage = LaunchStage.StageReady, SessionId = started.SessionId });
        registry.Disconnect(connection);
        Assert.Equal(EndSessionOutcome.EndSessionOffline, registry.EndSession("m-1", "shop-a").Outcome);
    }

    [Fact]
    public void StartSession_refuses_while_a_start_is_in_progress_unless_it_went_quiet_for_3_minutes()
    {
        var time = new ManualTime();
        var registry = new AgentRegistry(time);
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var first = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.True(first.Accepted);

        Assert.Equal(AgentRegistry.InProgressError, registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response.Error);

        // Progress keeps it in progress.
        time.Advance(TimeSpan.FromMinutes(2));
        registry.OnHeartbeat(connection, new Heartbeat());
        registry.OnProgress(connection, new LaunchProgress { RequestId = first.RequestId, Stage = LaunchStage.StageDownloading, Percent = 10 });
        time.Advance(TimeSpan.FromMinutes(2));
        registry.OnHeartbeat(connection, new Heartbeat());
        Assert.Equal(AgentRegistry.InProgressError, registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response.Error);

        // Another computer is not affected.
        registry.Register(Hello("m-2"), "10.0.0.2", null);
        Assert.True(registry.StartSession("m-2", false, "Bob", ServerUrl, true, true).Response.Accepted);

        // Silent for 3 minutes: no longer blocks.
        time.Advance(TimeSpan.FromMinutes(1));
        registry.OnHeartbeat(connection, new Heartbeat());
        Assert.True(registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response.Accepted);
    }

    [Fact]
    public async Task Stream_end_fails_starts_that_had_not_reached_a_final_stage()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        using var watch = registry.SubscribeLaunch(start.RequestId)!;
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageDownloading });

        Assert.True(registry.Disconnect(connection, out var failed));

        Assert.Equal(start.RequestId, Assert.Single(failed).RequestId);
        var seen = await ReadAll(watch.Reader);
        Assert.Equal(new[] { LaunchStage.StageQueued, LaunchStage.StageDownloading, LaunchStage.StageFailed }, seen.Select(p => p.Stage));
        Assert.Equal(AgentRegistry.DisconnectedDetail, seen[^1].Detail);

        // A start the agent never picked up is failed too, so the next one isn't blocked.
        connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var second = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.True(second.Accepted, second.Error);
        registry.Disconnect(connection, out failed);
        Assert.Equal(LaunchStage.StageFailed, registry.GetLatestProgress(second.RequestId)!.Stage);
    }

    [Fact]
    public void Stream_end_leaves_a_ready_session_alone()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageReady });

        Assert.True(registry.Disconnect(connection, out var failed));
        Assert.Empty(failed);
        Assert.Equal(LaunchStage.StageReady, registry.GetLatestProgress(start.RequestId)!.Stage);
    }

    [Fact]
    public void A_replaced_stream_ending_does_not_fail_starts()
    {
        var registry = new AgentRegistry(new ManualTime());
        var old = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        registry.Register(Hello("m-1"), "10.0.0.1", null);

        Assert.False(registry.Disconnect(old, out var failed));
        Assert.Empty(failed);
        Assert.Equal(LaunchStage.StageQueued, registry.GetLatestProgress(start.RequestId)!.Stage);
    }

    // ---------------------------------------------------------------- progress fan-out

    [Fact]
    public async Task Progress_is_replayed_then_fanned_out_until_a_terminal_stage()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;

        using var first = registry.SubscribeLaunch(start.RequestId)!;
        using var second = registry.SubscribeLaunch(start.RequestId)!;
        Assert.Null(registry.SubscribeLaunch(Guid.NewGuid().ToString()));

        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageDownloading, Percent = 35, Detail = "Downloading 45 of 129 MB" });
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageReady });

        foreach (var subscription in new[] { first, second })
        {
            var seen = await ReadAll(subscription.Reader);
            Assert.Equal(new[] { LaunchStage.StageQueued, LaunchStage.StageDownloading, LaunchStage.StageReady }, seen.Select(p => p.Stage));
            Assert.Equal(35, seen[1].Percent);
            Assert.All(seen, p => Assert.Equal(start.SessionId, p.SessionId));
        }

        var agent = registry.GetAgent("m-1")!;
        Assert.True(agent.SessionRunning);
        Assert.Equal(start.SessionId, agent.SessionId);

        // ENDED after READY is still recorded, and clears the session.
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageEnded, SessionId = start.SessionId });
        agent = registry.GetAgent("m-1")!;
        Assert.False(agent.SessionRunning);
        Assert.Equal(string.Empty, agent.SessionId);

        using var late = registry.SubscribeLaunch(start.RequestId)!;
        Assert.Equal(new[] { LaunchStage.StageEnded }, (await ReadAll(late.Reader)).Select(p => p.Stage));

        // Nothing after a final stage.
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageFailed });
        Assert.Equal(LaunchStage.StageEnded, registry.GetLatestProgress(start.RequestId)!.Stage);
    }

    [Fact]
    public async Task Declined_ends_the_watch_and_leaves_no_session()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        using var watch = registry.SubscribeLaunch(start.RequestId)!;

        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageWaitingForConsent });
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageDeclined, Detail = "The person at the computer declined the session" });

        Assert.Equal(new[] { LaunchStage.StageQueued, LaunchStage.StageWaitingForConsent, LaunchStage.StageDeclined }, (await ReadAll(watch.Reader)).Select(p => p.Stage));
        Assert.False(registry.GetAgent("m-1")!.SessionRunning);
        Assert.True(registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response.Accepted);
    }

    [Fact]
    public async Task An_unanswered_consent_prompt_ends_the_watch_and_leaves_no_session()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        using var watch = registry.SubscribeLaunch(start.RequestId)!;

        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageWaitingForConsent });
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageConsentTimedOut, Detail = "No answer within the consent time limit" });

        Assert.Equal(new[] { LaunchStage.StageQueued, LaunchStage.StageWaitingForConsent, LaunchStage.StageConsentTimedOut }, (await ReadAll(watch.Reader)).Select(p => p.Stage));
        Assert.True(AgentRegistry.IsWatchTerminal(LaunchStage.StageConsentTimedOut));
        Assert.False(registry.GetAgent("m-1")!.SessionRunning);
        // The launch is over, so the same computer can be asked again straight away.
        Assert.True(registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response.Accepted);
    }

    [Fact]
    public void An_agent_cannot_report_progress_for_another_agents_launch()
    {
        var registry = new AgentRegistry(new ManualTime());
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        var other = registry.Register(Hello("m-2"), "10.0.0.2", null);
        var start = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;

        Assert.Null(registry.OnProgress(other, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageReady }));
        Assert.Equal(LaunchStage.StageQueued, registry.GetLatestProgress(start.RequestId)!.Stage);
        Assert.False(registry.GetAgent("m-1")!.SessionRunning);
    }

    // ---------------------------------------------------------------- in-process gRPC round trip

    [Fact]
    public async Task Connect_StartSession_and_progress_round_trip_over_grpc()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var options = new CallOptions(cancellationToken: ct);
        var admin = new CallOptions(new Metadata { { "x-admin-key", "admin-secret" } }, cancellationToken: ct);
        await using var relay = await Relay.StartAsync(AgentOptions.Create("key-1,key-2", ServerUrl + "/", "admin-secret"));
        var invoker = relay.Invoker;

        using var connect = invoker.AsyncDuplexStreamingCall(ConnectMethod, null, options);
        await connect.RequestStream.WriteAsync(new AgentMessage { Hello = Hello("m-1", "key-2", ConsentMode.ConsentUnattendedAllowed) });

        await WaitFor(async () => (await invoker.AsyncUnaryCall(GetAgentsMethod, null, options, new Empty())).Agents.Any(a => a.MachineId == "m-1" && a.Online), ct);

        var start = await invoker.AsyncUnaryCall(StartSessionMethod, null, admin, new StartSessionRequest { MachineId = "m-1", Unattended = true, Requester = "Bob" });
        Assert.True(start.Accepted, start.Error);
        Assert.Equal($"{ServerUrl}/mirror/{start.SessionId}", start.AppUrl);

        Assert.True(await connect.ResponseStream.MoveNext(ct));
        var launch = connect.ResponseStream.Current.Launch;
        Assert.NotNull(launch);
        Assert.Equal(start.RequestId, launch.RequestId);
        Assert.Equal(start.SessionId, launch.SessionId);
        Assert.Equal(ServerUrl, launch.ServerUrl);
        Assert.Equal("Bob", launch.Requester);
        Assert.False(launch.RequireConsent);

        using var watch = invoker.AsyncServerStreamingCall(WatchLaunchMethod, null, options, new WatchLaunchRequest { RequestId = start.RequestId });
        Assert.True(await watch.ResponseStream.MoveNext(ct));
        Assert.Equal(LaunchStage.StageQueued, watch.ResponseStream.Current.Stage);

        await connect.RequestStream.WriteAsync(new AgentMessage { Progress = new LaunchProgress { RequestId = launch.RequestId, Stage = LaunchStage.StageDownloading, Percent = 40, SessionId = launch.SessionId } });
        Assert.True(await watch.ResponseStream.MoveNext(ct));
        Assert.Equal(LaunchStage.StageDownloading, watch.ResponseStream.Current.Stage);
        Assert.Equal(40, watch.ResponseStream.Current.Percent);

        await connect.RequestStream.WriteAsync(new AgentMessage { Progress = new LaunchProgress { RequestId = launch.RequestId, Stage = LaunchStage.StageReady, SessionId = launch.SessionId } });
        Assert.True(await watch.ResponseStream.MoveNext(ct));
        Assert.Equal(LaunchStage.StageReady, watch.ResponseStream.Current.Stage);
        Assert.False(await watch.ResponseStream.MoveNext(ct));

        await WaitFor(async () => (await invoker.AsyncUnaryCall(GetAgentsMethod, null, options, new Empty())).Agents.Any(a => a.SessionRunning && a.SessionId == start.SessionId), ct);
        var busy = await invoker.AsyncUnaryCall(StartSessionMethod, null, admin, new StartSessionRequest { MachineId = "m-1" });
        Assert.False(busy.Accepted);
        Assert.Equal(AgentRegistry.BusyError, busy.Error);

        // Agent closes its side: the relay ends the call and marks it offline.
        await connect.RequestStream.CompleteAsync();
        Assert.False(await connect.ResponseStream.MoveNext(ct));
        await WaitFor(async () => (await invoker.AsyncUnaryCall(GetAgentsMethod, null, options, new Empty())).Agents.Single(a => a.MachineId == "m-1").Online == false, ct);
    }

    [Fact]
    public async Task Connect_requires_AgentHello_first_and_an_accepted_key()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var options = new CallOptions(cancellationToken: timeout.Token);
        await using var relay = await Relay.StartAsync(AgentOptions.Create("key-1", null));

        using (var call = relay.Invoker.AsyncDuplexStreamingCall(ConnectMethod, null, options))
        {
            await call.RequestStream.WriteAsync(new AgentMessage { Heartbeat = new Heartbeat() });
            var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(timeout.Token));
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }

        using (var call = relay.Invoker.AsyncDuplexStreamingCall(ConnectMethod, null, options))
        {
            await call.RequestStream.WriteAsync(new AgentMessage { Hello = Hello("m-1", "wrong") });
            var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(timeout.Token));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        }

        Assert.Empty(relay.Registry.GetAgentList().Agents);
    }

    [Fact]
    public async Task Connect_refuses_every_agent_when_keys_are_required_and_none_are_set()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var options = new CallOptions(cancellationToken: timeout.Token);
        await using var relay = await Relay.StartAsync(AgentOptions.Create(null, null, requireKeys: true));

        foreach (var key in new[] { "any-key", "" })
        {
            using var call = relay.Invoker.AsyncDuplexStreamingCall(ConnectMethod, null, options);
            await call.RequestStream.WriteAsync(new AgentMessage { Hello = Hello("m-1", key) });
            var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(timeout.Token));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Equal(PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy.NoKeysConfiguredReason, ex.Status.Detail);
        }

        Assert.Empty(relay.Registry.GetAgentList().Agents);
    }

    [Fact]
    public async Task StartSession_and_CancelLaunch_need_the_admin_key_when_one_is_set()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var none = new CallOptions(cancellationToken: ct);
        var wrong = new CallOptions(new Metadata { { "x-admin-key", "nope" } }, cancellationToken: ct);
        var right = new CallOptions(new Metadata { { "x-admin-key", "admin-secret" } }, cancellationToken: ct);
        await using var relay = await Relay.StartAsync(AgentOptions.Create(null, null, "admin-secret"));
        relay.Registry.Register(Hello("m-1"), "10.0.0.1", null);
        var request = new StartSessionRequest { MachineId = "m-1" };

        foreach (var options in new[] { none, wrong })
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () => await relay.Invoker.AsyncUnaryCall(StartSessionMethod, null, options, request));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Equal(ConsentPolicy.AdminKeyRequired, ex.Status.Detail);
        }

        // Reading stays open.
        Assert.Single((await relay.Invoker.AsyncUnaryCall(GetAgentsMethod, null, none, new Empty())).Agents);

        var start = await relay.Invoker.AsyncUnaryCall(StartSessionMethod, null, right, request);
        Assert.True(start.Accepted, start.Error);

        var cancel = new CancelCommand { RequestId = start.RequestId };
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await relay.Invoker.AsyncUnaryCall(CancelLaunchMethod, null, wrong, cancel));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        await relay.Invoker.AsyncUnaryCall(CancelLaunchMethod, null, right, cancel);

        var end = new EndSessionRequest { MachineId = "m-1" };
        var endDenied = await Assert.ThrowsAsync<RpcException>(async () => await relay.Invoker.AsyncUnaryCall(EndSessionMethod, null, wrong, end));
        Assert.Equal(StatusCode.PermissionDenied, endDenied.StatusCode);
        var ended = await relay.Invoker.AsyncUnaryCall(EndSessionMethod, null, right, end);
        Assert.Equal(EndSessionOutcome.EndSessionRequested, ended.Outcome);
        Assert.Equal(start.RequestId, ended.RequestId);
    }

    [Fact]
    public async Task WatchAgents_pushes_the_list_on_change()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var options = new CallOptions(cancellationToken: timeout.Token);
        await using var relay = await Relay.StartAsync(AgentOptions.Create(null, null));

        using var watch = relay.Invoker.AsyncServerStreamingCall(WatchAgentsMethod, null, options, new Empty());
        Assert.True(await watch.ResponseStream.MoveNext(timeout.Token));
        Assert.Empty(watch.ResponseStream.Current.Agents);

        relay.Registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.True(await watch.ResponseStream.MoveNext(timeout.Token));
        Assert.Equal("m-1", Assert.Single(watch.ResponseStream.Current.Agents).MachineId);
    }

    // ---------------------------------------------------------------- UpdateRpcd

    [Fact]
    public async Task UpdateRpcd_forwards_the_command_and_returns_the_agents_snapshot()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);

        var task = registry.UpdateRpcdAsync("m-1", " stable ", " 1.2.0-ci.996.1 ", approved: false, approvedBy: " owner ", true, true, null, CancellationToken.None);
        Assert.True(connection.Commands.TryRead(out var command));
        Assert.Equal(HubCommand.BodyOneofCase.UpdateRpcd, command!.BodyCase);
        Assert.Equal("stable", command.UpdateRpcd.Channel);
        Assert.Equal("1.2.0-ci.996.1", command.UpdateRpcd.Version);
        Assert.False(command.UpdateRpcd.Approved);
        Assert.Equal("owner", command.UpdateRpcd.ApprovedBy);
        Assert.False(string.IsNullOrEmpty(command.UpdateRpcd.RequestId));

        // The fleet agent mints its own request id and does not echo the one on the command.
        var checking = new UpdateRpcdProgress
        {
            RequestId = "agent-req",
            Stage = "checking",
            Detail = "Checking the official GitHub release...",
            Channel = "stable",
        };
        Assert.Equal("checking", registry.OnUpdateProgress(connection, checking)!.Stage);

        var first = await task;
        Assert.Equal("agent-req", first.Progress!.RequestId);
        Assert.Equal("checking", first.Progress.Stage);
        Assert.Equal("PC-m-1", first.MachineName);

        var succeeded = new UpdateRpcdProgress
        {
            RequestId = "agent-req",
            Stage = "succeeded",
            Terminal = true,
            OldVersion = "1.2.0",
            NewVersion = "1.2.0-ci.996.1",
            Channel = "stable",
            Detail = "RPCD 1.2.0-ci.996.1 is installed (was 1.2.0).",
        };
        registry.OnUpdateProgress(connection, succeeded);
        var latest = registry.GetLatestUpdate("m-1");
        Assert.Equal("1.2.0", latest!.OldVersion);
        Assert.Equal("1.2.0-ci.996.1", latest.NewVersion);
        Assert.True(latest.Terminal);
    }

    [Fact]
    public async Task UpdateRpcd_refuses_a_bad_key_an_offline_computer_and_another_shop_without_sending()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, "shop-a");

        var denied = await registry.UpdateRpcdAsync("m-1", "stable", "", true, null, adminKeyConfigured: true, adminKeyValid: false, null, CancellationToken.None);
        Assert.True(denied.Denied);
        Assert.Equal(ConsentPolicy.AdminKeyRequired, denied.Error);
        Assert.False(connection.Commands.TryRead(out _));

        var otherShop = await registry.UpdateRpcdAsync("m-1", "stable", "", true, null, true, true, "shop-b", CancellationToken.None);
        Assert.True(otherShop.NotFound);
        Assert.Equal(AgentRegistry.UnknownAgentError, otherShop.Error);
        Assert.False(connection.Commands.TryRead(out _));

        registry.Disconnect(connection);
        var offline = await registry.UpdateRpcdAsync("m-1", "stable", "", true, null, true, true, "shop-a", CancellationToken.None);
        Assert.True(offline.Offline);
        Assert.Equal(AgentRegistry.OfflineError, offline.Error);
        Assert.False(connection.Commands.TryRead(out _));

        var missing = await registry.UpdateRpcdAsync("nope", "stable", "", true, null, true, true, null, CancellationToken.None);
        Assert.True(missing.NotFound);
        Assert.Equal(AgentRegistry.UnknownAgentError, missing.Error);

        var none = await registry.UpdateRpcdAsync("", "stable", "", true, null, true, true, null, CancellationToken.None);
        Assert.Equal(AgentRegistry.NoAgentError, none.Error);
    }

    [Fact]
    public async Task UpdateRpcd_reports_an_agent_that_never_answers_as_too_old()
    {
        var registry = new AgentRegistry(new ManualTime()) { UpdateAckTimeout = TimeSpan.FromMilliseconds(80) };
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);

        var outcome = await registry.UpdateRpcdAsync("m-1", "prerelease", "", approved: true, approvedBy: null, true, true, null, CancellationToken.None);

        Assert.True(outcome.TooOld);
        Assert.Equal(AgentRegistry.TooOldForUpdateError, outcome.Error);
        Assert.True(connection.Commands.TryRead(out var command));
        Assert.Equal(HubCommand.BodyOneofCase.UpdateRpcd, command!.BodyCase);
        Assert.True(command.UpdateRpcd.Approved);
        Assert.Equal("", command.UpdateRpcd.ApprovedBy);
        Assert.Null(registry.GetLatestUpdate("m-1"));
    }

    [Fact]
    public async Task UpdateRpcd_does_not_start_a_second_update_while_the_first_is_unanswered()
    {
        var registry = new AgentRegistry(new ManualTime()) { UpdateAckTimeout = TimeSpan.FromSeconds(2) };
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var first = registry.UpdateRpcdAsync("m-1", "stable", "", true, null, true, true, null, CancellationToken.None);
        Assert.True(connection.Commands.TryRead(out _));

        var second = await registry.UpdateRpcdAsync("m-1", "stable", "", true, null, true, true, null, CancellationToken.None);
        Assert.Equal("refused", second.Progress!.Stage);
        Assert.Equal(AgentRegistry.UpdateInProgressError, second.Progress.Error);
        Assert.True(second.Progress.Terminal);
        Assert.False(connection.Commands.TryRead(out _));

        registry.OnUpdateProgress(connection, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "checking", Channel = "stable" });
        Assert.Equal("checking", (await first).Progress!.Stage);
    }

    [Fact]
    public async Task WatchUpdateRpcd_replays_the_latest_then_streams_until_the_landed_version()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var started = registry.UpdateRpcdAsync("m-1", "stable", "", true, "owner", true, true, null, CancellationToken.None);
        Assert.True(connection.Commands.TryRead(out _));
        registry.OnUpdateProgress(connection, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "checking", Channel = "stable" });
        await started;

        var watch = registry.OpenUpdateWatch("m-1", "agent-req", true, true, null);
        Assert.NotNull(watch.Subscription);
        Assert.True(watch.Subscription!.Replayed);
        Assert.True(connection.Commands.TryRead(out var status));
        Assert.Equal(HubCommand.BodyOneofCase.UpdateRpcdStatus, status!.BodyCase);
        Assert.Equal("agent-req", status.UpdateRpcdStatus.RequestId);

        registry.OnUpdateProgress(connection, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "downloading", Percent = 40, Channel = "stable" });
        registry.OnUpdateProgress(connection, new UpdateRpcdProgress
        {
            RequestId = "agent-req",
            Stage = "succeeded",
            Terminal = true,
            OldVersion = "1.2.0",
            NewVersion = "1.2.0-ci.1000.1",
            Channel = "stable",
        });

        var seen = await ReadAll(watch.Subscription.Reader);
        Assert.Equal(new[] { "checking", "downloading", "succeeded" }, seen.Select(p => p.Stage));
        Assert.Equal("1.2.0", seen[^1].OldVersion);
        Assert.Equal("1.2.0-ci.1000.1", seen[^1].NewVersion);
    }

    [Fact]
    public async Task WatchUpdateRpcd_with_an_empty_request_follows_the_latest_and_asks_for_it()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, "shop-a");
        var started = registry.UpdateRpcdAsync("m-1", "prerelease", "", true, null, true, true, "shop-a", CancellationToken.None);
        Assert.True(connection.Commands.TryRead(out _));
        registry.OnUpdateProgress(connection, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "installing", Channel = "prerelease" });
        await started;

        var hidden = registry.OpenUpdateWatch("m-1", "", true, true, "shop-b");
        Assert.True(hidden.NotFound);
        Assert.False(connection.Commands.TryRead(out _));

        var watch = registry.OpenUpdateWatch("m-1", "", true, true, "shop-a");
        Assert.True(connection.Commands.TryRead(out var status));
        Assert.Equal("", status!.UpdateRpcdStatus.RequestId);

        registry.OnUpdateProgress(connection, new UpdateRpcdProgress
        {
            RequestId = "agent-req",
            Stage = "succeeded",
            Terminal = true,
            OldVersion = "1.2.0",
            NewVersion = "1.2.0-ci.1000.1",
        });
        var seen = await ReadAll(watch.Subscription!.Reader);
        Assert.Equal(new[] { "installing", "succeeded" }, seen.Select(p => p.Stage));
    }

    [Fact]
    public async Task WatchUpdateRpcd_asks_the_agent_when_the_relay_has_no_snapshot()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);

        var watch = registry.OpenUpdateWatch("m-1", "", true, true, null);
        Assert.False(watch.Subscription!.Replayed);
        Assert.True(connection.Commands.TryRead(out var status));
        Assert.Equal(HubCommand.BodyOneofCase.UpdateRpcdStatus, status!.BodyCase);
        Assert.Equal("", status.UpdateRpcdStatus.RequestId);

        registry.OnUpdateProgress(connection, new UpdateRpcdProgress
        {
            RequestId = "agent-req",
            Stage = "succeeded",
            Terminal = true,
            OldVersion = "1.2.0",
            NewVersion = "1.2.0-ci.1000.1",
        });

        var seen = Assert.Single(await ReadAll(watch.Subscription.Reader));
        Assert.Equal("succeeded", seen.Stage);
        Assert.Equal("1.2.0", seen.OldVersion);
        Assert.Equal("1.2.0-ci.1000.1", seen.NewVersion);
    }

    [Fact]
    public async Task An_update_survives_disconnect_and_the_reconnect_asks_for_the_snapshot()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var started = registry.UpdateRpcdAsync("m-1", "stable", "", true, null, true, true, null, CancellationToken.None);
        Assert.True(connection.Commands.TryRead(out _));
        registry.OnUpdateProgress(connection, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "installing", NewVersion = "1.2.0-ci.1000.1" });
        await started;

        registry.Disconnect(connection);
        Assert.Equal("installing", registry.GetLatestUpdate("m-1")!.Stage);

        var again = registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.True(again.Commands.TryRead(out var status));
        Assert.Equal(HubCommand.BodyOneofCase.UpdateRpcdStatus, status!.BodyCase);
        Assert.Equal("agent-req", status.UpdateRpcdStatus.RequestId);

        registry.OnUpdateProgress(again, new UpdateRpcdProgress
        {
            RequestId = "agent-req",
            Stage = "succeeded",
            Terminal = true,
            OldVersion = "1.2.0",
            NewVersion = "1.2.0-ci.1000.1",
        });
        Assert.Equal("succeeded", registry.GetLatestUpdate("m-1")!.Stage);
    }

    [Fact]
    public async Task An_agent_cannot_report_another_agents_update()
    {
        var registry = new AgentRegistry(new ManualTime());
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var other = registry.Register(Hello("m-2"), "10.0.0.2", null);
        var started = registry.UpdateRpcdAsync("m-1", "stable", "", true, null, true, true, null, CancellationToken.None);
        Assert.True(connection.Commands.TryRead(out _));
        registry.OnUpdateProgress(connection, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "checking" });
        await started;

        Assert.Null(registry.OnUpdateProgress(other, new UpdateRpcdProgress { RequestId = "agent-req", Stage = "succeeded", Terminal = true, NewVersion = "9.9.9" }));
        Assert.Equal("checking", registry.GetLatestUpdate("m-1")!.Stage);
    }

    [Fact]
    public async Task UpdateRpcd_and_WatchUpdateRpcd_use_the_admin_key_shop_scope_and_ticket_decision()
    {
        var registry = new AgentRegistry(new ManualTime()) { UpdateAckTimeout = TimeSpan.FromMilliseconds(80) };
        registry.Register(Hello("m-1"), "10.0.0.1", null, "shop-a");
        var options = AgentOptions.Create(null, null, "admin-secret");
        var open = new AgentAdminService(Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentAdminService>.Instance, registry, options);
        var context = new CallContext(CancellationToken.None);

        var missingKey = await Assert.ThrowsAsync<RpcException>(() => open.UpdateRpcd(new UpdateRpcdAdminRequest { MachineId = "m-1", Channel = "stable", Approved = true }, context));
        Assert.Equal(StatusCode.PermissionDenied, missingKey.StatusCode);
        Assert.Equal(ConsentPolicy.AdminKeyRequired, missingKey.Status.Detail);

        var denied = new FixedPolicy(new PeakSWC.RemoteWebView.AgentAdminAccessDecision(Allowed: false, GroupScope: null, UnattendedAuthorized: false, DenialReason: "Sign in to continue."));
        var signedOut = new AgentAdminService(Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentAdminService>.Instance, registry, options, denied);
        var ticket = await Assert.ThrowsAsync<RpcException>(() => signedOut.UpdateRpcd(new UpdateRpcdAdminRequest { MachineId = "m-1", Channel = "stable", Approved = true }, context));
        Assert.Equal(StatusCode.PermissionDenied, ticket.StatusCode);
        Assert.Equal("Sign in to continue.", ticket.Status.Detail);
        var watchTicket = await Assert.ThrowsAsync<RpcException>(() => signedOut.WatchUpdateRpcd(new UpdateRpcdWatchRequest { MachineId = "m-1" }, new ListWriter<UpdateRpcdProgress>(), context));
        Assert.Equal("Sign in to continue.", watchTicket.Status.Detail);

        var shop = new FixedPolicy(new PeakSWC.RemoteWebView.AgentAdminAccessDecision(Allowed: true, GroupScope: "shop-a", UnattendedAuthorized: true));
        var scoped = new AgentAdminService(Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentAdminService>.Instance, registry, AgentOptions.Create(null, null), shop);
        // m-1 is in shop-a and online, but it never answers the command, so the agent is too old to install updates.
        var tooOld = await Assert.ThrowsAsync<RpcException>(() => scoped.UpdateRpcd(new UpdateRpcdAdminRequest { MachineId = "m-1", Channel = "stable", Approved = true }, context));
        Assert.Equal(StatusCode.FailedPrecondition, tooOld.StatusCode);
        Assert.Equal(AgentRegistry.TooOldForUpdateError, tooOld.Status.Detail);
        var watchOld = await Assert.ThrowsAsync<RpcException>(() => scoped.WatchUpdateRpcd(new UpdateRpcdWatchRequest { MachineId = "m-1" }, new ListWriter<UpdateRpcdProgress>(), context));
        Assert.Equal(StatusCode.FailedPrecondition, watchOld.StatusCode);
        Assert.Equal(AgentRegistry.TooOldForUpdateError, watchOld.Status.Detail);

        registry.Register(Hello("m-2"), "10.0.0.2", null, "shop-b");
        var hidden = await Assert.ThrowsAsync<RpcException>(() => scoped.UpdateRpcd(new UpdateRpcdAdminRequest { MachineId = "m-2", Channel = "stable", Approved = true }, context));
        Assert.Equal(StatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(AgentRegistry.UnknownAgentError, hidden.Status.Detail);

        var offline = await Assert.ThrowsAsync<RpcException>(() => scoped.WatchUpdateRpcd(new UpdateRpcdWatchRequest { MachineId = "m-2" }, new ListWriter<UpdateRpcdProgress>(), context));
        Assert.Equal(StatusCode.NotFound, offline.StatusCode);
    }

    [Fact]
    public async Task Connect_UpdateRpcd_and_WatchUpdateRpcd_round_trip_over_grpc()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var options = new CallOptions(cancellationToken: ct);
        var admin = new CallOptions(new Metadata { { "x-admin-key", "admin-secret" } }, cancellationToken: ct);
        await using var relay = await Relay.StartAsync(AgentOptions.Create("key-1", ServerUrl, "admin-secret"));
        var invoker = relay.Invoker;

        using var connect = invoker.AsyncDuplexStreamingCall(ConnectMethod, null, options);
        await connect.RequestStream.WriteAsync(new AgentMessage { Hello = Hello("m-1", "key-1") });
        await WaitFor(async () => (await invoker.AsyncUnaryCall(GetAgentsMethod, null, options, new Empty())).Agents.Any(a => a.MachineId == "m-1" && a.Online), ct);

        var updateTask = invoker.AsyncUnaryCall(UpdateRpcdMethod, null, admin, new UpdateRpcdAdminRequest
        {
            MachineId = "m-1",
            Channel = "stable",
            Version = "1.2.0-ci.1000.1",
            Approved = true,
            ApprovedBy = "owner",
        });
        Assert.True(await connect.ResponseStream.MoveNext(ct));
        var command = connect.ResponseStream.Current.UpdateRpcd;
        Assert.NotNull(command);
        Assert.Equal("stable", command.Channel);
        Assert.Equal("1.2.0-ci.1000.1", command.Version);
        Assert.True(command.Approved);
        Assert.Equal("owner", command.ApprovedBy);

        await connect.RequestStream.WriteAsync(new AgentMessage
        {
            UpdateRpcd = new UpdateRpcdProgress { RequestId = "agent-req", Stage = "checking", Channel = "stable", Detail = "Checking the official GitHub release..." },
        });
        var first = await updateTask;
        Assert.Equal("checking", first.Stage);
        Assert.Equal("agent-req", first.RequestId);

        using var watch = invoker.AsyncServerStreamingCall(WatchUpdateRpcdMethod, null, admin, new UpdateRpcdWatchRequest { MachineId = "m-1", RequestId = "agent-req" });
        Assert.True(await watch.ResponseStream.MoveNext(ct));
        Assert.Equal("checking", watch.ResponseStream.Current.Stage);

        Assert.True(await connect.ResponseStream.MoveNext(ct));
        Assert.Equal(HubCommand.BodyOneofCase.UpdateRpcdStatus, connect.ResponseStream.Current.BodyCase);
        Assert.Equal("agent-req", connect.ResponseStream.Current.UpdateRpcdStatus.RequestId);

        await connect.RequestStream.WriteAsync(new AgentMessage
        {
            UpdateRpcd = new UpdateRpcdProgress
            {
                RequestId = "agent-req",
                Stage = "succeeded",
                Terminal = true,
                OldVersion = "1.2.0",
                NewVersion = "1.2.0-ci.1000.1",
                Channel = "stable",
            },
        });
        Assert.True(await watch.ResponseStream.MoveNext(ct));
        Assert.Equal("succeeded", watch.ResponseStream.Current.Stage);
        Assert.Equal("1.2.0", watch.ResponseStream.Current.OldVersion);
        Assert.Equal("1.2.0-ci.1000.1", watch.ResponseStream.Current.NewVersion);
        Assert.False(await watch.ResponseStream.MoveNext(ct));

        var wrong = new CallOptions(new Metadata { { "x-admin-key", "nope" } }, cancellationToken: ct);
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await invoker.AsyncUnaryCall(UpdateRpcdMethod, null, wrong, new UpdateRpcdAdminRequest { MachineId = "m-1", Channel = "stable", Approved = true }));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
    }

    private static async Task<List<UpdateRpcdProgress>> ReadAll(ChannelReader<UpdateRpcdProgress> reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var list = new List<UpdateRpcdProgress>();
        await foreach (var item in reader.ReadAllAsync(cts.Token))
            list.Add(item);
        return list;
    }

    private sealed class ListWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.CompletedTask;
        public Task WriteAsync(T message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // ---------------------------------------------------------------- helpers

    private static AgentHello Hello(string machineId, string key = "", ConsentMode ceiling = ConsentMode.ConsentAlwaysAsk) => new()
    {
        MachineId = machineId,
        EnrollmentKey = key,
        MachineName = "PC-" + machineId,
        OsVersion = "Windows 11 Pro",
        AgentVersion = "1.0.0",
        ConsentCeiling = ceiling,
        Lifetime = Lifetime.Persistent,
    };

    private static async Task<List<LaunchProgress>> ReadAll(ChannelReader<LaunchProgress> reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var list = new List<LaunchProgress>();
        await foreach (var item in reader.ReadAllAsync(cts.Token))
            list.Add(item);
        return list;
    }

    private static async Task WaitFor(Func<Task<bool>> condition, CancellationToken ct)
    {
        while (!await condition())
            await Task.Delay(20, ct);
    }

    private static Marshaller<T> Proto<T>(MessageParser<T> parser) where T : IMessage<T> =>
        Marshallers.Create<T>(m => m.ToByteArray(), bytes => parser.ParseFrom(bytes));

    private static readonly Method<AgentMessage, HubCommand> ConnectMethod =
        new(MethodType.DuplexStreaming, "rwvagent.AgentHub", "Connect", Proto(AgentMessage.Parser), Proto(HubCommand.Parser));
    private static readonly Method<Empty, AgentList> GetAgentsMethod =
        new(MethodType.Unary, "rwvagent.AgentAdmin", "GetAgents", Proto(Empty.Parser), Proto(AgentList.Parser));
    private static readonly Method<Empty, AgentList> WatchAgentsMethod =
        new(MethodType.ServerStreaming, "rwvagent.AgentAdmin", "WatchAgents", Proto(Empty.Parser), Proto(AgentList.Parser));
    private static readonly Method<StartSessionRequest, StartSessionResponse> StartSessionMethod =
        new(MethodType.Unary, "rwvagent.AgentAdmin", "StartSession", Proto(StartSessionRequest.Parser), Proto(StartSessionResponse.Parser));
    private static readonly Method<CancelCommand, Empty> CancelLaunchMethod =
        new(MethodType.Unary, "rwvagent.AgentAdmin", "CancelLaunch", Proto(CancelCommand.Parser), Proto(Empty.Parser));
    private static readonly Method<EndSessionRequest, EndSessionResponse> EndSessionMethod =
        new(MethodType.Unary, "rwvagent.AgentAdmin", "EndSession", Proto(EndSessionRequest.Parser), Proto(EndSessionResponse.Parser));
    private static readonly Method<WatchLaunchRequest, LaunchProgress> WatchLaunchMethod =
        new(MethodType.ServerStreaming, "rwvagent.AgentAdmin", "WatchLaunch", Proto(WatchLaunchRequest.Parser), Proto(LaunchProgress.Parser));
    private static readonly Method<UpdateRpcdAdminRequest, UpdateRpcdProgress> UpdateRpcdMethod =
        new(MethodType.Unary, "rwvagent.AgentAdmin", "UpdateRpcd", Proto(UpdateRpcdAdminRequest.Parser), Proto(UpdateRpcdProgress.Parser));
    private static readonly Method<UpdateRpcdWatchRequest, UpdateRpcdProgress> WatchUpdateRpcdMethod =
        new(MethodType.ServerStreaming, "rwvagent.AgentAdmin", "WatchUpdateRpcd", Proto(UpdateRpcdWatchRequest.Parser), Proto(UpdateRpcdProgress.Parser));

    private sealed class Relay : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly GrpcChannel _channel;

        private Relay(IHost host, GrpcChannel channel, AgentRegistry registry)
        {
            _host = host;
            _channel = channel;
            Registry = registry;
            Invoker = channel.CreateCallInvoker();
        }

        public AgentRegistry Registry { get; }
        public CallInvoker Invoker { get; }

        public static async Task<Relay> StartAsync(AgentOptions options)
        {
            var registry = new AgentRegistry();
            var host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddGrpc();
                        services.AddSingleton(options);
                        services.AddSingleton(registry);
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGrpcService<AgentHubService>();
                            endpoints.MapGrpcService<AgentAdminService>();
                        });
                    });
                })
                .StartAsync();

            var server = host.GetTestServer();
            var channel = GrpcChannel.ForAddress(server.BaseAddress, new GrpcChannelOptions { HttpHandler = server.CreateHandler() });
            return new Relay(host, channel, registry);
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task Platform_admin_with_null_group_scope_starts_unattended_session_without_consent()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("machine-1", ceiling: ConsentMode.ConsentUnattendedAllowed), "10.0.0.1", null);

        var policy = new FixedPolicy(new PeakSWC.RemoteWebView.AgentAdminAccessDecision(Allowed: true, GroupScope: null, UnattendedAuthorized: true));
        var service = new AgentAdminService(Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentAdminService>.Instance, registry, AgentOptions.Create(null, null), policy);

        var response = await service.StartSession(new StartSessionRequest { MachineId = "machine-1", Unattended = true }, new CallContext(CancellationToken.None));
        Assert.True(response.Accepted);

        Assert.True(connection.Commands.TryRead(out var cmd));
        Assert.NotNull(cmd.Launch);
        Assert.False(cmd.Launch.RequireConsent);
    }

    private sealed class FixedPolicy(PeakSWC.RemoteWebView.AgentAdminAccessDecision decision) : PeakSWC.RemoteWebView.IAgentTenantPolicy
    {
        public PeakSWC.RemoteWebView.AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, AgentOptions options) => throw new NotImplementedException();
        public PeakSWC.RemoteWebView.AgentAdminAccessDecision CheckAdminAccess(HttpContext? httpContext, string? adminKeyHeader, AgentOptions options) => decision;
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class CallContext : ServerCallContext
    {
        private readonly CancellationToken token;

        public CallContext(CancellationToken token, HttpContext? httpContext = null)
        {
            this.token = token;
            // Where Grpc.AspNetCore's GetHttpContext() looks when the context is not its own.
            if (httpContext != null)
                UserState["__HttpContext"] = httpContext;
        }

        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => token;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
