// Marks the types generated from src/Protos/agent.proto as experimental (enums can't be partial, so ConsentMode,
// Lifetime and LaunchStage are not marked; they are only reachable through the marked types).
using System.Diagnostics.CodeAnalysis;

namespace PeakSWC.RemoteWebView.Agent
{
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public static partial class AgentHub;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public static partial class AgentAdmin;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class AgentMessage;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class AgentHello;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class Heartbeat;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class LaunchProgress;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class HubCommand;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class LaunchCommand;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class CancelCommand;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class AgentInfo;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class AgentList;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class StartSessionRequest;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class StartSessionResponse;
    [Experimental(global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.AgentRegistry, UrlFormat = global::PeakSWC.RemoteWebView.Agents.ExperimentalApi.UrlFormat)]
    public sealed partial class WatchLaunchRequest;
}
