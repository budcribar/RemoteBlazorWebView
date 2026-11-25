using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Agents;

namespace StaticContentCache.Tests;

public class AgentAlertsTests
{
    private static AgentHello Hello(string machineId, long revision = 0) => new()
    {
        MachineId = machineId,
        MachineName = "PC-" + machineId,
        AgentVersion = "1.0.0",
        AlertSettingsRevision = revision,
    };

    private static AgentAlert Alert(long seq, string kind = "low_disk") => new()
    {
        AlertId = Guid.NewGuid().ToString(),
        Seq = seq,
        Kind = kind,
        Severity = AlertSeverity.AlertWarning,
        InstanceId = "C:",
        FirstSeen = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
        LastSeen = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
        Summary = new string('x', 1000),
    };

    [Fact]
    public void Uploads_ReachOnlyWatchersOfThatShop_Trimmed()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, group: "shop-a");
        using var all = registry.SubscribeAlerts(null);
        using var shopA = registry.SubscribeAlerts("shop-a");
        using var shopB = registry.SubscribeAlerts("shop-b");

        var alert = Alert(1);
        for (int i = 0; i < 12; i++) alert.Facts.Add(new AlertFact { Name = "f" + i, Value = "v" });
        alert.Facts.Add(new AlertFact { Name = " ", Value = "dropped" });
        for (int i = 0; i < 6; i++) alert.SourceEvents.Add(new AlertSourceEvent { Channel = "System", Provider = "disk", EventId = 7, RecordId = i });
        alert.RelatedChecks.Add("DriveHealth");
        alert.RelatedChecks.Add("drivehealth");
        Assert.Equal(2, registry.OnAlerts(connection, new AlertBatch { Alerts = { alert, Alert(2) } }));

        Assert.True(shopA.Reader.TryRead(out var upload));
        Assert.Equal(("m-1", "PC-m-1", "shop-a", 2), (upload.MachineId, upload.MachineName, upload.Group, upload.Alerts.Count));
        var first = upload.Alerts[0];
        Assert.Equal(300, first.Summary.Length);
        Assert.Equal(AgentRegistry.MaxFactsPerAlert, first.Facts.Count);
        Assert.Equal([2L, 3, 4, 5], first.SourceEvents.Select(e => e.RecordId)); // the newest four
        Assert.Equal(["DriveHealth"], first.RelatedChecks);
        Assert.Equal(1, first.Count); // an agent that sent 0 still means one
        Assert.True(all.Reader.TryRead(out _));
        Assert.False(shopB.Reader.TryRead(out _));
    }

    [Fact]
    public void AlertsWithoutIdKindOrSeq_AreDropped_AndNoWatcherMeansTheAgentKeepsThem()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.Equal(0, registry.OnAlerts(connection, new AlertBatch { Alerts = { Alert(1) } }));
        using var live = registry.SubscribeAlerts(null);
        Assert.Equal(0, registry.OnAlerts(connection, new AlertBatch { Alerts = { Alert(0), Alert(2, kind: ""), new AgentAlert { Seq = 3, Kind = "low_disk" } } }));
    }

    [Fact]
    public void AStreamThatWasReplaced_CannotUpload()
    {
        var registry = new AgentRegistry();
        var old = registry.Register(Hello("m-1"), "10.0.0.1", null);
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        using var sub = registry.SubscribeAlerts(null);
        Assert.Equal(0, registry.OnAlerts(old, new AlertBatch { Alerts = { Alert(1) } }));
    }

    [Fact]
    public void Commands_GoToOnlineAgentsInScope_AndAreNeverQueuedWhileOffline()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, group: "shop-a");
        var settings = new AlertSettingsCommand
        {
            Revision = 4,
            Enabled = true,
            DisabledKinds = { "app_crash", "APP_CRASH", " ", "overheating" },
            LowDiskFreeGb = double.NaN,
            MemoryCommitPercent = 90,
        };

        Assert.False(registry.SetAlertSettings("m-1", settings, "shop-b", out var error));
        Assert.Equal(AgentRegistry.UnknownAgentError, error);
        Assert.False(registry.SetAlertSettings("m-1", new AlertSettingsCommand(), "shop-a", out _)); // revision 0
        Assert.True(registry.SetAlertSettings("m-1", settings, "shop-a", out _));
        Assert.True(connection.Commands.TryRead(out var sent));
        Assert.Equal(["app_crash", "overheating"], sent.AlertSettings.DisabledKinds);
        Assert.Equal(0, sent.AlertSettings.LowDiskFreeGb); // not a number: the agent's default
        Assert.Equal(90, sent.AlertSettings.MemoryCommitPercent);

        Assert.True(registry.AckAlerts("m-1", 9, "shop-a", out _));
        Assert.True(connection.Commands.TryRead(out var ack));
        Assert.Equal(9, ack.AlertsAck.ThroughSeq);
        Assert.False(registry.AckAlerts("m-1", 0, "shop-a", out _));

        Assert.True(registry.SetAlertState("m-1", "alert-1", AlertState.AlertHandled, null, out _));
        Assert.True(connection.Commands.TryRead(out var state));
        Assert.Equal(("alert-1", AlertState.AlertHandled), (state.AlertState.AlertId, state.AlertState.State));
        Assert.False(registry.SetAlertState("m-1", "alert-1", AlertState.AlertOpen, null, out _)); // only handled or cleared
        Assert.False(registry.SetAlertState("m-1", " ", AlertState.AlertCleared, null, out _));

        registry.Disconnect(connection);
        Assert.False(registry.SetAlertSettings("m-1", settings, null, out error));
        Assert.Equal(AgentRegistry.OfflineError, error);
        Assert.False(registry.SetAlertState("m-1", "alert-1", AlertState.AlertCleared, null, out _));
        Assert.False(connection.Commands.TryRead(out _));
    }

    [Fact]
    public void TheSavedRevision_IsListed_FromHelloAndHeartbeat()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1", revision: 2), "10.0.0.1", null);
        Assert.Equal(2, registry.GetAgent("m-1")!.AlertSettingsRevision);
        registry.OnHeartbeat(connection, new Heartbeat { AlertSettingsRevision = 6 });
        Assert.Equal(6, registry.GetAgent("m-1")!.AlertSettingsRevision);
    }
}
