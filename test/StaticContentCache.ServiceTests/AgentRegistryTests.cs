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
        Assert.Equal("Bob", AgentAdminService.GetRequester(new StartSessionRequest { Requester = "Bob" }, anonymous));
        Assert.Equal(AgentAdminService.DefaultRequester, AgentAdminService.GetRequester(new StartSessionRequest(), anonymous));

        var signedIn = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("name", "Alice Admin") }, "test")) };
        Assert.Equal("Alice Admin", AgentAdminService.GetRequester(new StartSessionRequest { Requester = "Bob" }, new CallContext(CancellationToken.None, signedIn)));
        Assert.Equal("Alice Admin", AgentAdminService.GetRequester(null, new CallContext(CancellationToken.None, signedIn)));
        Assert.Equal("Alice Admin (AI connector)",
            AgentAdminService.GetRequester(new StartSessionRequest { Requester = "Alice Admin (AI connector)" }, new CallContext(CancellationToken.None, signedIn)));
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
        registry.OnProgress(connection, new LaunchProgress { RequestId = start.RequestId, Stage = LaunchStage.StageDeclined, Detail = "No answer within 2 minutes" });

        Assert.Equal(new[] { LaunchStage.StageQueued, LaunchStage.StageWaitingForConsent, LaunchStage.StageDeclined }, (await ReadAll(watch.Reader)).Select(p => p.Stage));
        Assert.False(registry.GetAgent("m-1")!.SessionRunning);
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
