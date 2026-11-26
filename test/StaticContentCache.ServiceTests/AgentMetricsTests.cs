using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Agents;

namespace StaticContentCache.Tests;

public class AgentMetricsTests
{
    private static AgentHello Hello(string machineId, long revision = 0) => new()
    {
        MachineId = machineId,
        MachineName = "PC-" + machineId,
        AgentVersion = "1.0.0",
        MetricsScheduleRevision = revision,
    };

    private static MetricsBatch Batch(params long[] seqs)
    {
        var batch = new MetricsBatch();
        foreach (var seq in seqs)
        {
            var run = new MetricsRun { RunId = Guid.NewGuid().ToString(), Seq = seq, CheckId = "BatteryPower", Outcome = "OK", Summary = new string('x', 1000) };
            run.Metrics.Add(new MetricValue { Id = "battery.health", Label = "Battery health", Value = 87, Unit = "%", Better = MetricDirection.MetricHigherIsBetter });
            run.Metrics.Add(new MetricValue { Id = "bad", Value = double.NaN });
            batch.Runs.Add(run);
        }
        return batch;
    }

    [Fact]
    public void Uploads_ReachOnlyWatchersOfThatShop_Trimmed()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, group: "shop-a");
        using var all = registry.SubscribeMetrics(null);
        using var shopA = registry.SubscribeMetrics("shop-a");
        using var shopB = registry.SubscribeMetrics("shop-b");

        Assert.Equal(2, registry.OnMetrics(connection, Batch(1, 2)));

        Assert.True(shopA.Reader.TryRead(out var upload));
        Assert.Equal(("m-1", "PC-m-1", "shop-a", 2), (upload.MachineId, upload.MachineName, upload.Group, upload.Runs.Count));
        Assert.Equal(300, upload.Runs[0].Summary.Length);
        Assert.Equal("battery.health", Assert.Single(upload.Runs[0].Metrics).Id);
        Assert.True(all.Reader.TryRead(out _));
        Assert.False(shopB.Reader.TryRead(out _));
    }

    [Fact]
    public void NoWatcher_DeliversToNobody_SoTheAgentKeepsItsRuns()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        Assert.Equal(0, registry.OnMetrics(connection, Batch(1)));
        var sub = registry.SubscribeMetrics(null);
        sub.Dispose();
        Assert.Equal(0, registry.OnMetrics(connection, Batch(2)));
        // Runs without an id or sequence are dropped.
        using var live = registry.SubscribeMetrics(null);
        Assert.Equal(0, registry.OnMetrics(connection, new MetricsBatch { Runs = { new MetricsRun { Seq = 3 }, new MetricsRun { RunId = "x" } } }));
    }

    [Fact]
    public void AStreamThatWasReplaced_CannotUpload()
    {
        var registry = new AgentRegistry();
        var old = registry.Register(Hello("m-1"), "10.0.0.1", null);
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        using var sub = registry.SubscribeMetrics(null);
        Assert.Equal(0, registry.OnMetrics(old, Batch(1)));
    }

    [Fact]
    public void Commands_GoToOnlineAgentsInScope_AndAreNeverQueuedWhileOffline()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, group: "shop-a");
        var schedule = new MetricsScheduleCommand { Revision = 3, Enabled = true, IntervalMinutes = 1440, CheckIds = { "BatteryPower", "batterypower", " ", "DriveHealth" } };

        Assert.False(registry.SetMetricsSchedule("m-1", schedule, "shop-b", out var error));
        Assert.Equal(AgentRegistry.UnknownAgentError, error);
        Assert.True(registry.SetMetricsSchedule("m-1", schedule, "shop-a", out _));
        Assert.True(connection.Commands.TryRead(out var sent));
        Assert.Equal(["BatteryPower", "DriveHealth"], sent.MetricsSchedule.CheckIds);

        Assert.True(registry.AckMetrics("m-1", 7, "shop-a", out _));
        Assert.True(connection.Commands.TryRead(out var ack));
        Assert.Equal(7, ack.MetricsAck.ThroughSeq);
        Assert.False(registry.AckMetrics("m-1", 0, "shop-a", out _));

        Assert.True(registry.CollectMetrics("m-1", null, out _));
        Assert.True(connection.Commands.TryRead(out var collect));
        Assert.False(string.IsNullOrEmpty(collect.CollectMetrics.RequestId));

        registry.Disconnect(connection);
        Assert.False(registry.CollectMetrics("m-1", null, out error));
        Assert.Equal(AgentRegistry.OfflineError, error);
        Assert.False(registry.SetMetricsSchedule("m-1", schedule, null, out _));
        Assert.False(connection.Commands.TryRead(out _));
    }

    [Fact]
    public void CancelUpdate_GoesToTheOnlineAgentInScope_AndACancelledUpdateIsTerminal()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null, group: "shop-a");

        Assert.False(registry.CancelUpdateRpcd("m-1", "req-1", "shop-b", out var error));
        Assert.Equal(AgentRegistry.UnknownAgentError, error);
        Assert.True(registry.CancelUpdateRpcd("m-1", " req-1 ", "shop-a", out _));
        Assert.True(connection.Commands.TryRead(out var cancel));
        Assert.Equal("req-1", cancel.CancelUpdateRpcd.RequestId);

        Assert.True(AgentRegistry.IsUpdateTerminal(new UpdateRpcdProgress { RequestId = "req-1", Stage = "cancelled" }));
        Assert.False(AgentRegistry.IsUpdateTerminal(new UpdateRpcdProgress { RequestId = "req-1", Stage = "waiting" }));

        registry.Disconnect(connection);
        Assert.False(registry.CancelUpdateRpcd("m-1", "req-1", null, out error));
        Assert.Equal(AgentRegistry.OfflineError, error);
        Assert.False(connection.Commands.TryRead(out _));
    }

    [Fact]
    public void TheSavedRevision_IsListed_FromHelloAndHeartbeat()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1", revision: 2), "10.0.0.1", null);
        Assert.Equal(2, registry.GetAgent("m-1")!.MetricsScheduleRevision);
        registry.OnHeartbeat(connection, new Heartbeat { MetricsScheduleRevision = 5 });
        Assert.Equal(5, registry.GetAgent("m-1")!.MetricsScheduleRevision);
    }
}
